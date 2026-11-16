using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;
using Windows.Media.Control;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// What is playing in apps that report to Windows' media controls: title, artist, album
/// art, track progress, and play/pause/next/previous (plus shuffle and repeat where the app
/// supports them). Two instances exist: the Spotify widget, and "A tocar" for everything else.
/// </summary>
internal sealed class MediaPlugin : PollingPlugin
{
    private readonly string _id;
    private readonly string _name;
    private readonly Func<string, bool> _accepts;
    private readonly string _icon;
    private readonly string _color;
    private readonly string? _openUri;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _sessionId;
    private string? _trackKey;
    private ImageSource? _art;

    private MediaPlugin(string id, string name, Func<string, bool> accepts, string icon, string color, string? openUri)
    {
        _id = id;
        _name = name;
        _accepts = accepts;
        _icon = icon;
        _color = color;
        _openUri = openUri;
    }

    public override string Id => _id;

    public override string Name => _name;

    public override PluginPlacement DefaultPlacement => PluginPlacement.Left;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <summary>Spotify, with a link to open the app.</summary>
    public static MediaPlugin Spotify() =>
        new("spotify", "Spotify", IsSpotify, "spotify", "#1ED760", "spotify:");

    /// <summary>Any other player; also Spotify when the Spotify widget is switched off.</summary>
    public static MediaPlugin NowPlaying(bool spotifyHasOwnWidget) =>
        new("nowplaying", "A tocar", aumid => !spotifyHasOwnWidget || !IsSpotify(aumid), "music", "#FF5E8A", null);

    public static bool IsSpotify(string aumid) => aumid.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    public override bool TryHandle(string action)
    {
        GlobalSystemMediaTransportControlsSession? session = CurrentSession();
        if (session is null || !action.StartsWith("media.", StringComparison.Ordinal))
        {
            return false;
        }

        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = session.GetPlaybackInfo();
        Task? task = action switch
        {
            "media.playpause" => session.TryTogglePlayPauseAsync().AsTask(),
            "media.next" => session.TrySkipNextAsync().AsTask(),
            "media.previous" => session.TrySkipPreviousAsync().AsTask(),
            "media.shuffle" => session.TryChangeShuffleActiveAsync(!(info?.IsShuffleActive ?? false)).AsTask(),
            "media.repeat" => session.TryChangeAutoRepeatModeAsync(NextRepeat(info?.AutoRepeatMode)).AsTask(),
            _ => null,
        };

        task?.ContinueWith(_ => Refresh(), TaskScheduler.Default);
        return task is not null;
    }

    protected override async Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellation).ConfigureAwait(false);

        GlobalSystemMediaTransportControlsSession? session = CurrentSession();
        if (session is null)
        {
            _trackKey = null;
            return new PluginUpdate(new PluginOutput { Hidden = true });
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties media = await session.TryGetMediaPropertiesAsync().AsTask(cancellation).ConfigureAwait(false);
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = session.GetPlaybackInfo();
        bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        string title = media?.Title ?? string.Empty;
        string artist = media?.Artist ?? string.Empty;
        string album = media?.AlbumTitle ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return new PluginUpdate(new PluginOutput { Hidden = true });
        }

        string key = title + "|" + artist + "|" + album;
        if (key != _trackKey)
        {
            _trackKey = key;
            _art = await LoadArtAsync(media, cancellation).ConfigureAwait(false);
        }

        (double? progress, string? times) = Progress(session, playing);
        string app = FriendlyApp(session.SourceAppUserModelId);
        string line = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";

        var buttons = new List<PluginAction>
        {
            new() { Title = "Anterior", Icon = "previous", Action = "media.previous" },
            new() { Title = playing ? "Pausa" : "Reproduzir", Icon = playing ? "pause" : "play", Action = "media.playpause" },
            new() { Title = "Seguinte", Icon = "next", Action = "media.next" },
        };

        if (info?.Controls is { IsShuffleEnabled: true })
        {
            buttons.Insert(0, new PluginAction { Title = info.IsShuffleActive == true ? "Aleatório: ligado" : "Aleatório: desligado", Icon = "shuffle", Action = "media.shuffle" });
        }

        if (info?.Controls is { IsRepeatEnabled: true })
        {
            buttons.Add(new PluginAction { Title = "Repetir: " + RepeatName(info.AutoRepeatMode), Icon = "repeat", Action = "media.repeat" });
        }

        var rows = new List<PluginRow>();
        if (progress is { } p)
        {
            rows.Add(new PluginRow { Icon = playing ? "play" : "pause", Title = playing ? "A reproduzir" : "Em pausa", Value = times, Progress = p });
        }

        var menu = new List<PluginAction>(buttons);
        if (_openUri is not null)
        {
            menu.Add(new PluginAction { Title = "Abrir " + _name, Icon = "external", Open = _openUri });
        }

        var output = new PluginOutput
        {
            Text = Shorten(line, 30),
            Icon = playing ? _icon : "pause",
            Color = _color,
            Bars = progress is { } bar ? [new PluginBar { Value = bar, Color = _color }] : [],
            Tooltip = $"{title}\n{artist}{(string.IsNullOrWhiteSpace(album) ? "" : " · " + album)}\n{app}",
            Click = new PluginAction { Action = "panel" },
            Menu = menu,
            Panel = new PluginPanel
            {
                Title = title,
                Subtitle = string.Join(" · ", new[] { artist, album, app }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Rows = rows,
                Buttons = buttons,
            },
        };

        return new PluginUpdate(output, _art);
    }

    private GlobalSystemMediaTransportControlsSession? CurrentSession()
    {
        if (_manager is null)
        {
            return null;
        }

        // Prefer the session Windows considers current, else any session this widget accepts.
        GlobalSystemMediaTransportControlsSession? current = _manager.GetCurrentSession();
        if (current is not null && _accepts(current.SourceAppUserModelId ?? string.Empty))
        {
            _sessionId = current.SourceAppUserModelId;
            return current;
        }

        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = _manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? match =
            sessions.FirstOrDefault(s => s.SourceAppUserModelId == _sessionId && _accepts(s.SourceAppUserModelId ?? string.Empty))
            ?? sessions.FirstOrDefault(s => _accepts(s.SourceAppUserModelId ?? string.Empty)
                                            && s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? sessions.FirstOrDefault(s => _accepts(s.SourceAppUserModelId ?? string.Empty));

        _sessionId = match?.SourceAppUserModelId;
        return match;
    }

    private static (double? Progress, string? Times) Progress(GlobalSystemMediaTransportControlsSession session, bool playing)
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = session.GetTimelineProperties();
        if (timeline is null || timeline.EndTime <= TimeSpan.Zero)
        {
            return (null, null);
        }

        TimeSpan position = timeline.Position;
        if (playing)
        {
            // The position is only reported now and then; extrapolate from when it was.
            TimeSpan since = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (since > TimeSpan.Zero && since < TimeSpan.FromHours(1))
            {
                position += since;
            }
        }

        TimeSpan length = timeline.EndTime - timeline.StartTime;
        if (length <= TimeSpan.Zero)
        {
            return (null, null);
        }

        position = position < TimeSpan.Zero ? TimeSpan.Zero : position > length ? length : position;
        return (position / length, $"{Clock(position)} / {Clock(length)}");
    }

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private static Windows.Media.MediaPlaybackAutoRepeatMode NextRepeat(Windows.Media.MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
        Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
        _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
    };

    private static string RepeatName(Windows.Media.MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        Windows.Media.MediaPlaybackAutoRepeatMode.List => "lista",
        Windows.Media.MediaPlaybackAutoRepeatMode.Track => "faixa",
        _ => "não",
    };

    private static async Task<ImageSource?> LoadArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? media, CancellationToken cancellation)
    {
        if (media?.Thumbnail is null)
        {
            return null;
        }

        try
        {
            using var winrtStream = await media.Thumbnail.OpenReadAsync().AsTask(cancellation).ConfigureAwait(false);
            using Stream source = winrtStream.AsStreamForRead();
            var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellation).ConfigureAwait(false);
            buffer.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = buffer;
            image.DecodePixelWidth = 256;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug("Media: no album art: " + ex.Message);
            return null;
        }
    }

    private static string FriendlyApp(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return string.Empty;
        }

        if (IsSpotify(aumid))
        {
            return "Spotify";
        }

        string name = aumid.Split('!')[0];
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(name) : name;
        int underscore = name.IndexOf('_');
        if (underscore > 0)
        {
            name = name[..underscore];
        }

        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
}
