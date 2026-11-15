namespace LinearBar.Core.Config;

/// <summary>
/// User configuration, persisted as JSON (see <see cref="SettingsStore"/>). Every property
/// has a sensible default so a missing or partial file still yields a working bar.
/// </summary>
public sealed class BarSettings
{
    /// <summary>Version of the settings schema, for future migrations.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Height of the bar strip in device-independent pixels. This is the screen space reserved.</summary>
    public double BarHeight { get; set; } = 52;

    /// <summary>Opacity of the bar background, 0.2–1.</summary>
    public double BarOpacity { get; set; } = 0.88;

    /// <summary>Hide the Windows taskbar while LinearBar runs (it is restored on exit).</summary>
    public bool HideWindowsTaskbar { get; set; } = true;

    /// <summary>Show notification-area (system tray) icons.</summary>
    public bool ShowTray { get; set; } = true;

    public DockSettings Dock { get; set; } = new();

    public MetricsSettings Metrics { get; set; } = new();

    public ClockSettings Clock { get; set; } = new();

    /// <summary>Apps pinned to the dock, in display order.</summary>
    public List<PinnedApp> Pinned { get; set; } = [];

    /// <summary>Set once the Windows taskbar pins have been imported, so it only happens on first run.</summary>
    public bool ImportedTaskbarPins { get; set; }

    /// <summary>
    /// Replaces missing sections with defaults and clamps every value into its supported
    /// range. Called after loading so the rest of the app never has to validate settings.
    /// </summary>
    public void Normalize()
    {
        Dock ??= new DockSettings();
        Metrics ??= new MetricsSettings();
        Clock ??= new ClockSettings();
        Pinned ??= [];

        BarHeight = ClampOrDefault(BarHeight, 32, 96, 52);
        BarOpacity = ClampOrDefault(BarOpacity, 0.2, 1, 0.88);

        Dock.IconSize = ClampOrDefault(Dock.IconSize, 16, Math.Max(16, BarHeight - 10), Math.Min(40, BarHeight - 10));
        Dock.Spacing = ClampOrDefault(Dock.Spacing, 0, 32, 8);
        Dock.MaxScale = ClampOrDefault(Dock.MaxScale, 1, 3, 1.75);
        Dock.LensRadius = ClampOrDefault(Dock.LensRadius, 1, 6, 2.5);

        Metrics.IntervalMs = (int)ClampOrDefault(Metrics.IntervalMs, 250, 10_000, 1000);
        Metrics.HistoryLength = (int)ClampOrDefault(Metrics.HistoryLength, 10, 600, 60);

        if (string.IsNullOrWhiteSpace(Clock.TimeFormat))
        {
            Clock.TimeFormat = "HH:mm";
        }

        if (string.IsNullOrWhiteSpace(Clock.DateFormat))
        {
            Clock.DateFormat = "ddd, d MMM";
        }

        // Drop unusable pins and duplicates (same app pinned twice).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Pinned.RemoveAll(pin => pin is null || AppKey.ForPinned(pin) is not { } key || !seen.Add(key));
    }

    private static double ClampOrDefault(double value, double min, double max, double fallback)
    {
        return double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }
}

public sealed class DockSettings
{
    /// <summary>Resting icon size in device-independent pixels.</summary>
    public double IconSize { get; set; } = 40;

    /// <summary>Gap between neighbouring icons.</summary>
    public double Spacing { get; set; } = 8;

    /// <summary>Enable the macOS-style magnification on hover.</summary>
    public bool Magnify { get; set; } = true;

    /// <summary>Scale of the icon under the cursor (1 = no magnification).</summary>
    public double MaxScale { get; set; } = 1.75;

    /// <summary>How far the magnification reaches on each side of the cursor, in icons.</summary>
    public double LensRadius { get; set; } = 2.5;

    /// <summary>Show the app name above the hovered icon.</summary>
    public bool ShowLabels { get; set; } = true;
}

public sealed class MetricsSettings
{
    /// <summary>Show the live graphs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sampling interval in milliseconds.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Number of samples kept per graph.</summary>
    public int HistoryLength { get; set; } = 60;

    public bool ShowCpu { get; set; } = true;

    public bool ShowMemory { get; set; } = true;

    public bool ShowNetwork { get; set; } = true;

    /// <summary>Show battery charge (only appears on machines that have a battery).</summary>
    public bool ShowBattery { get; set; } = true;
}

public sealed class ClockSettings
{
    public bool Show { get; set; } = true;

    /// <summary>.NET custom time format, e.g. <c>HH:mm</c> or <c>HH:mm:ss</c>.</summary>
    public string TimeFormat { get; set; } = "HH:mm";

    public bool ShowDate { get; set; } = true;

    /// <summary>.NET custom date format, e.g. <c>ddd, d MMM</c>.</summary>
    public string DateFormat { get; set; } = "ddd, d MMM";
}

/// <summary>An app pinned to the dock.</summary>
public sealed class PinnedApp
{
    /// <summary>Display name; when empty the name is taken from the file.</summary>
    public string? Name { get; set; }

    /// <summary>What to launch: an executable, a shortcut (.lnk) or any shell-openable path.</summary>
    public string? Path { get; set; }

    /// <summary>Command-line arguments passed when launching <see cref="Path"/>.</summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// Executable that <see cref="Path"/> resolves to, when it is a shortcut. Running windows
    /// are matched against this to decide whether the pinned app is running.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>Application User Model ID, for Store/UWP apps that have no executable to launch.</summary>
    public string? Aumid { get; set; }
}
