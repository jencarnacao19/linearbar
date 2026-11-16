using System.Windows;
using System.Windows.Media;
using LinearBar.Core.Config;
using LinearBar.Infrastructure;

namespace LinearBar.Visuals;

/// <summary>
/// Applies the appearance settings to the running app: colours and font become dynamic
/// resources (so every element updates live), the glass gets its tint and corners.
/// </summary>
internal static class ThemeManager
{
    public static readonly Color DefaultAccent = Color.FromRgb(0x5A, 0xA9, 0xFF);

    /// <summary>The accent in effect, resolved from "system" when needed.</summary>
    public static Color Accent { get; private set; } = DefaultAccent;

    public static void Apply(BarSettings settings, bool blurBehind)
    {
        AppearanceSettings appearance = settings.Appearance;
        ResourceDictionary resources = Application.Current.Resources;

        Accent = appearance.Accent == "system" ? SystemAccent() ?? DefaultAccent : Parse(appearance.Accent) ?? DefaultAccent;
        Color text = Parse(appearance.TextColor) ?? Color.FromRgb(0xF2, 0xF2, 0xF5);
        Color glass = Parse(appearance.GlassColor) ?? Color.FromRgb(16, 16, 22);

        resources["AccentBrush"] = Frozen(Accent);
        resources["TextBrush"] = Frozen(text);
        resources["SubtleTextBrush"] = Frozen(Color.FromArgb(0xB0, text.R, text.G, text.B));

        // Hover and press highlights follow the text colour, so light themes work too.
        resources["HoverBrush"] = Frozen(Color.FromArgb(0x1F, text.R, text.G, text.B));
        resources["PressedBrush"] = Frozen(Color.FromArgb(0x33, text.R, text.G, text.B));

        try
        {
            resources["UiFont"] = new FontFamily(appearance.FontFamily);
        }
        catch (ArgumentException ex)
        {
            Log.Warn($"Font '{appearance.FontFamily}' unusable: {ex.Message}");
        }

        Glass.Configure(blurBehind, settings.BarOpacity, appearance.CornerSmoothing, glass);
    }

    /// <summary>Windows' accent colour (the light variant, which reads well on dark glass).</summary>
    public static Color? SystemAccent()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            Windows.UI.Color c = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception ex)
        {
            Log.Debug("System accent unavailable: " + ex.Message);
            return null;
        }
    }

    public static Color? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
