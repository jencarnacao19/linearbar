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
    public double BarHeight { get; set; } = 64;

    /// <summary>Strength of the dark tint of the glass, 0.2–1 (higher is darker and less see-through).</summary>
    public double BarOpacity { get; set; } = 0.5;

    /// <summary>Hide the Windows taskbar while LinearBar runs (it is restored on exit).</summary>
    public bool HideWindowsTaskbar { get; set; } = true;

    /// <summary>Show notification-area (system tray) icons.</summary>
    public bool ShowTray { get; set; } = true;

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>What goes where in the bar, and the bar's position and style.</summary>
    public LayoutSettings Layout { get; set; } = new();

    public DockSettings Dock { get; set; } = new();

    public MetricsSettings Metrics { get; set; } = new();

    public ClockSettings Clock { get; set; } = new();

    /// <summary>
    /// Per-plugin settings, keyed by plugin id. Plugins missing here use their defaults;
    /// <c>"enabled": false</c> switches one off. Any other key is passed to the plugin.
    /// </summary>
    public Dictionary<string, PluginConfig> Plugins { get; set; } = [];

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
        Appearance ??= new AppearanceSettings();
        Layout ??= new LayoutSettings();
        Layout.Normalize();
        Metrics ??= new MetricsSettings();
        Clock ??= new ClockSettings();
        Pinned ??= [];
        Plugins ??= [];
        foreach (string key in Plugins.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            Plugins[key] = new PluginConfig();
        }

        BarHeight = ClampOrDefault(BarHeight, 40, 110, 64);
        BarOpacity = ClampOrDefault(BarOpacity, 0.2, 1, 0.5);
        Appearance.CornerSmoothing = ClampOrDefault(Appearance.CornerSmoothing, 0, 1, 0.6);
        Appearance.CornerRoundness = ClampOrDefault(Appearance.CornerRoundness, 0, 0.5, 0.36);
        Appearance.IslandMargin = ClampOrDefault(Appearance.IslandMargin, 0, 16, 5);
        Appearance.Accent = ValidColor(Appearance.Accent, "#5AA9FF", allowSystem: true);
        Appearance.GlassColor = ValidColor(Appearance.GlassColor, "#101016");
        Appearance.TextColor = ValidColor(Appearance.TextColor, "#F2F2F5");
        if (string.IsNullOrWhiteSpace(Appearance.FontFamily))
        {
            Appearance.FontFamily = "Segoe UI Variable Text, Segoe UI";
        }

        double maxIcon = Math.Max(16, LinearBar.Core.Dock.BarGeometry.MaxIconSize(BarHeight, Appearance.IslandMargin));
        Dock.IconSize = ClampOrDefault(Dock.IconSize, 16, maxIcon, Math.Min(40, maxIcon));
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

    /// <summary>Accepts "#RGB", "#RRGGBB" or "#AARRGGBB" (and "system" where allowed).</summary>
    internal static string ValidColor(string? value, string fallback, bool allowSystem = false)
    {
        string text = (value ?? string.Empty).Trim();
        if (allowSystem && text.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        bool hex = text.Length is 4 or 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);
        return hex ? text.ToUpperInvariant() : fallback;
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

    /// <summary>Put every icon on a squircle glass tile so all icons share one shape.</summary>
    public bool IconTiles { get; set; } = true;
}

public sealed class AppearanceSettings
{
    /// <summary>Blur what is behind the glass islands (frosted glass). Off: tinted glass only.</summary>
    public bool Blur { get; set; } = true;

    /// <summary>Corner smoothing of the squircles: 0 = ordinary rounded corners, 1 = smoothest.</summary>
    public double CornerSmoothing { get; set; } = 0.6;

    /// <summary>
    /// Corner radius of the islands as a fraction of their height: 0 = square, 0.5 = pill.
    /// </summary>
    public double CornerRoundness { get; set; } = 0.36;

    /// <summary>Gap between the islands and the screen edges, in pixels.</summary>
    public double IslandMargin { get; set; } = 5;

    /// <summary>Accent colour as "#RRGGBB", or "system" for the Windows accent colour.</summary>
    public string Accent { get; set; } = "#5AA9FF";

    /// <summary>Colour the glass is tinted with, "#RRGGBB".</summary>
    public string GlassColor { get; set; } = "#101016";

    /// <summary>Main text colour, "#RRGGBB".</summary>
    public string TextColor { get; set; } = "#F2F2F5";

    /// <summary>Font family for all text in the bar.</summary>
    public string FontFamily { get; set; } = "Segoe UI Variable Text, Segoe UI";
}

/// <summary>Where things are in the bar.</summary>
public sealed class LayoutSettings
{
    /// <summary>Widgets that can be placed in the zones, besides plugin ids.</summary>
    public static readonly IReadOnlyList<string> CoreItems = ["start", "dock", "cpu", "memory", "network", "battery", "tray", "clock", "gap"];

    /// <summary>"bottom" or "top" edge of the screen.</summary>
    public string Position { get; set; } = "bottom";

    /// <summary>"islands": floating glass islands; "bar": one continuous glass bar.</summary>
    public string Style { get; set; } = "islands";

    /// <summary>Items in the left zone, in order. "gap" starts a new island.</summary>
    public List<string> Left { get; set; } = ["start", "spotify", "nowplaying"];

    /// <summary>Items in the centre zone (by default just the dock).</summary>
    public List<string> Center { get; set; } = ["dock"];

    /// <summary>Items in the right zone.</summary>
    public List<string> Right { get; set; } = ["calendar", "weather", "github", "claude", "codex", "agy", "ping", "cpu", "memory", "network", "battery", "tray", "clock"];

    public bool IsTop => Position == "top";

    public bool IsContinuous => Style == "bar";

    /// <summary>All three zones, left to right.</summary>
    public IEnumerable<(string Name, List<string> Items)> Zones()
    {
        yield return ("left", Left);
        yield return ("center", Center);
        yield return ("right", Right);
    }

    /// <summary>Validates positions and style, and makes every item appear at most once.</summary>
    public void Normalize()
    {
        Position = Position?.Trim().ToLowerInvariant() == "top" ? "top" : "bottom";
        Style = Style?.Trim().ToLowerInvariant() == "bar" ? "bar" : "islands";
        Left ??= [];
        Center ??= [];
        Right ??= [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (List<string> zone in new[] { Left, Center, Right })
        {
            for (int i = 0; i < zone.Count; i++)
            {
                string item = (zone[i] ?? string.Empty).Trim().ToLowerInvariant();
                zone[i] = item;
            }

            // "gap" may repeat; everything else appears once, in its first zone.
            zone.RemoveAll(item => item.Length == 0 || (item != "gap" && !seen.Add(item)));

            // No leading, trailing or doubled gaps.
            for (int i = zone.Count - 1; i >= 0; i--)
            {
                bool gap = zone[i] == "gap";
                if (gap && (i == 0 || i == zone.Count - 1 || zone[i - 1] == "gap"))
                {
                    zone.RemoveAt(i);
                }
            }
        }
    }
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

/// <summary>Settings of one plugin.</summary>
public sealed class PluginConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>"left" or "right" island; null keeps the plugin's own choice.</summary>
    public string? Placement { get; set; }

    /// <summary>Plugin-specific options, e.g. <c>"city": "Lisboa"</c>.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement> Options { get; set; } = [];

    /// <summary>A string option, or null.</summary>
    public string? GetString(string name) =>
        Options.TryGetValue(name, out System.Text.Json.JsonElement value)
            ? value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    /// <summary>A numeric option, or null.</summary>
    public double? GetNumber(string name) =>
        Options.TryGetValue(name, out System.Text.Json.JsonElement value)
            && (value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.TryGetDouble(out double n)
                : double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n))
            ? n
            : null;
}
