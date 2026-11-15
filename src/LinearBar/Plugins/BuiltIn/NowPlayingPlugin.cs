using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;
using Windows.Media.Control;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// What is playing in any app that reports to Windows' media controls (Spotify, browsers,
/// the Media Player, …): title, artist, album art, and play/pause/next/previous.
/// </summary>
internal sealed class NowPlayingPlugin : PollingPlugin
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string? _trackKey;
    private ImageSource? _art;

    public override string Id => "nowplaying";

    public override string Name => "A tocar";

    public override PluginPlacement DefaultPlacement => PluginPlacement.Left;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(2);

    public override bool TryHandle(string action)
    {
        GlobalSystemMediaTransportControlsSession? session = _manager?.GetCurrentSession();
        if (session is null)
        {
            return false;
        }

        _ = action switch
        {
            "media.playpause" => session.TryTogglePlayPauseAsync().AsTask(),
            "media.next" => session.TrySkipNextAsync().AsTask(),
            "media.previous" => session.TrySkipPreviousAsync().AsTask(),
            _ => null,
        };

        Refresh();
        return action.StartsWith("media.", StringComparison.Ordinal);
    }

    protected override async Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellation).ConfigureAwait(false);

        GlobalSystemMediaTransportControlsSession? session = _manager.GetCurrentSession();
        if (session is null)
        {
            _trackKey = null;
            return new PluginUpdate(new PluginOutput { Hidden = true });
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties media = await session.TryGetMediaPropertiesAsync().AsTask(cancellation).ConfigureAwait(false);
        bool playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        string title = media?.Title ?? string.Empty;
        string artist = media?.Artist ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return new PluginUpdate(new PluginOutput { Hidden = true });
        }

        string key = title + "|" + artist + "|" + media?.AlbumTitle;
        if (key != _trackKey)
        {
            _trackKey = key;
            _art = await LoadArtAsync(media, cancellation).ConfigureAwait(false);
        }

        string app = FriendlyApp(session.SourceAppUserModelId);
        string line = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
        var output = new PluginOutput
        {
            Text = Shorten(line, 32),
            Icon = playing ? "music" : "pause",
            Color = "#FF5E8A",
            Tooltip = $"{title}\n{artist}{(string.IsNullOrWhiteSpace(media?.AlbumTitle) ? "" : " · " + media!.AlbumTitle)}\n{app}",
            Click = new PluginAction { Action = "panel" },
            Menu =
            [
                new PluginAction { Title = playing ? "Pausa" : "Reproduzir", Icon = playing ? "pause" : "play", Action = "media.playpause" },
                new PluginAction { Title = "Seguinte", Icon = "next", Action = "media.next" },
                new PluginAction { Title = "Anterior", Icon = "previous", Action = "media.previous" },
            ],
            Panel = new PluginPanel
            {
                Title = title,
                Subtitle = string.Join(" · ", new[] { artist, media?.AlbumTitle, app }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Buttons =
                [
                    new PluginAction { Title = "Anterior", Icon = "previous", Action = "media.previous" },
                    new PluginAction { Title = playing ? "Pausa" : "Reproduzir", Icon = playing ? "pause" : "play", Action = "media.playpause" },
                    new PluginAction { Title = "Seguinte", Icon = "next", Action = "media.next" },
                ],
            },
        };

        return new PluginUpdate(output, _art);
    }

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
            Log.Debug("Now playing: no album art: " + ex.Message);
            return null;
        }
    }

    private static string FriendlyApp(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return string.Empty;
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
