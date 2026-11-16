using System.Windows.Media;
using LinearBar.Core.Metrics;
using LinearBar.Infrastructure;

namespace LinearBar.Metrics;

/// <summary>Colours of the graphs.</summary>
internal static class Palette
{
    public static readonly Color Cpu = Color.FromRgb(0x5A, 0xA9, 0xFF);
    public static readonly Color Memory = Color.FromRgb(0xB7, 0x8C, 0xFF);
    public static readonly Color NetworkDown = Color.FromRgb(0x4F, 0xD1, 0xA5);
    public static readonly Color NetworkUp = Color.FromRgb(0xFF, 0xB4, 0x54);

    public static Brush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Vertical fade used to fill the area under a graph line.</summary>
    public static Brush Fade(Color color)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(0x70, color.R, color.G, color.B),
            Color.FromArgb(0x08, color.R, color.G, color.B),
            90);
        brush.Freeze();
        return brush;
    }
}

/// <summary>One live graph in the bar: a label, the current value and its recent history.</summary>
internal sealed class MetricItem : ObservableObject
{
    private string _valueText = string.Empty;
    private string _detail = string.Empty;
    private double _maximum;
    private int _version;

    public MetricItem(string key, string label, double width, double maximum, int historyLength, Color color, Color? secondaryColor = null)
    {
        Key = key;
        Label = label;
        Width = width;
        _maximum = maximum;
        History = new SampleHistory(historyLength);
        Stroke = Palette.Solid(color);
        Fill = Palette.Fade(color);

        if (secondaryColor is { } secondary)
        {
            SecondaryHistory = new SampleHistory(historyLength);
            SecondaryStroke = Palette.Solid(secondary);
        }
    }

    /// <summary>Layout id: "cpu", "memory" or "network".</summary>
    public string Key { get; }

    /// <summary>Short caption, e.g. "CPU".</summary>
    public string Label { get; }

    /// <summary>Width of the widget in device-independent pixels.</summary>
    public double Width { get; }

    public SampleHistory History { get; }

    /// <summary>Second series drawn as a line only (upload next to download), or null.</summary>
    public SampleHistory? SecondaryHistory { get; }

    public Brush Stroke { get; }

    public Brush Fill { get; }

    public Brush? SecondaryStroke { get; }

    /// <summary>Current value as text, e.g. "23 %".</summary>
    public string ValueText
    {
        get => _valueText;
        set => Set(ref _valueText, value);
    }

    /// <summary>Longer description shown as a tooltip.</summary>
    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>Value at the top of the graph.</summary>
    public double Maximum
    {
        get => _maximum;
        set => Set(ref _maximum, value);
    }

    /// <summary>Changes whenever a sample is added; the graph binds to it to know when to redraw.</summary>
    public int Version
    {
        get => _version;
        private set => Set(ref _version, value);
    }

    public void Add(double value, double? secondary = null)
    {
        History.Add(value);
        if (secondary is { } second)
        {
            SecondaryHistory?.Add(second);
        }

        Version++;
    }

    public void Resize(int historyLength)
    {
        History.Resize(historyLength);
        SecondaryHistory?.Resize(historyLength);
        Version++;
    }
}
