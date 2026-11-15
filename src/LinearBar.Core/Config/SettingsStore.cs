using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinearBar.Core.Config;

/// <summary>
/// Loads and saves <see cref="BarSettings"/> as a JSON file and, optionally, watches the
/// file so edits made in a text editor are applied while the bar is running.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private string? _lastKnownText;
    private bool _disposed;

    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Current = new BarSettings();
        Current.Normalize();
    }

    /// <summary>Default location: <c>%APPDATA%\LinearBar\settings.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LinearBar",
        "settings.json");

    /// <summary>Full path of the settings file.</summary>
    public string FilePath { get; }

    /// <summary>Settings in effect. Replaced, never mutated in place, when the file is reloaded.</summary>
    public BarSettings Current { get; private set; }

    /// <summary>Why the last load or save failed, or null when it succeeded.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Raised after the file was changed outside the app and reloaded successfully.
    /// Fires on a thread-pool thread; marshal to the UI thread before touching UI state.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Reads the settings file. A missing file is created with defaults. An unreadable or
    /// invalid file leaves <see cref="Current"/> untouched and records <see cref="LastError"/>,
    /// so a typo never takes the bar down.
    /// </summary>
    public BarSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                var defaults = new BarSettings();
                defaults.Normalize();
                Current = defaults;
                TryWrite(defaults);
                return Current;
            }

            TryRead();
            return Current;
        }
    }

    /// <summary>Writes <paramref name="settings"/> to disk and makes them current.</summary>
    public bool Save(BarSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            settings.Normalize();
            Current = settings;
            return TryWrite(settings);
        }
    }

    /// <summary>Applies <paramref name="change"/> to the current settings and saves them.</summary>
    public bool Update(Action<BarSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            change(Current);
            return Save(Current);
        }
    }

    /// <summary>Starts raising <see cref="Changed"/> when the file is edited externally.</summary>
    public void StartWatching()
    {
        lock (_gate)
        {
            if (_watcher is not null || _disposed)
            {
                return;
            }

            string directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            _reloadTimer = new Timer(_ => ReloadFromWatcher(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }
    }

    /// <summary>Serializes settings exactly as they are written to disk.</summary>
    public static string Serialize(BarSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    /// <summary>Parses settings JSON; throws <see cref="JsonException"/> when it is invalid.</summary>
    public static BarSettings Deserialize(string json)
    {
        var settings = JsonSerializer.Deserialize<BarSettings>(json, JsonOptions) ?? new BarSettings();
        settings.Normalize();
        return settings;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Editors save in several steps (truncate, write, rename); wait for them to finish.
        lock (_gate)
        {
            _reloadTimer?.Change(ReloadDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void ReloadFromWatcher()
    {
        bool changed;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            changed = TryRead();
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <returns>True when new, valid content was loaded.</returns>
    private bool TryRead()
    {
        try
        {
            string text = File.ReadAllText(FilePath);
            if (text == _lastKnownText)
            {
                // Our own write, or a save that did not change anything.
                return false;
            }

            BarSettings settings = Deserialize(text);
            _lastKnownText = text;
            Current = settings;
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private bool TryWrite(BarSettings settings)
    {
        try
        {
            string text = Serialize(settings);
            string directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            // Write to a temporary file first so a crash mid-write cannot corrupt the settings.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, FilePath, overwrite: true);

            _lastKnownText = text;
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
