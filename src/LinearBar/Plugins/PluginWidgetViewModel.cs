using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LinearBar.Core.Metrics;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;

namespace LinearBar.Plugins;

/// <summary>A gauge under a widget's text.</summary>
internal sealed record PluginBarViewModel(string? Label, double Value, Brush Fill, string? Detail);

/// <summary>One plugin's widget in the bar.</summary>
internal sealed class PluginWidgetViewModel : ObservableObject, IDisposable
{
    private static readonly Color DefaultAccent = Color.FromRgb(0x5A, 0xA9, 0xFF);

    private readonly IPlugin _plugin;
    private readonly Dispatcher _dispatcher;
    private PluginOutput _output = new() { Hidden = true };
    private ImageSource? _image;
    private Brush _accent = Freeze(new SolidColorBrush(DefaultAccent));
    private IReadOnlyList<PluginBarViewModel> _bars = [];
    private SampleHistory? _graph;
    private int _graphVersion;

    public PluginWidgetViewModel(IPlugin plugin, PluginPlacement placement)
    {
        _plugin = plugin;
        Placement = placement;
        _dispatcher = Dispatcher.CurrentDispatcher;
        plugin.Updated += OnUpdated;
    }

    public string Id => _plugin.Id;

    public string Name => _plugin.Name;

    public PluginPlacement Placement { get; }

    public IPlugin Plugin => _plugin;

    public PluginOutput Output => _output;

    public bool IsVisible => !_output.Hidden;

    public bool HasError => _output.Error is not null;

    public string? Text => _output.Error is not null ? "!" : _output.Text;

    public bool HasText => !string.IsNullOrEmpty(Text);

    public string? Icon => _output.Icon;

    /// <summary>Image from a built-in plugin (album art), drawn instead of the icon.</summary>
    public ImageSource? Image => _image;

    public string Tooltip => _output.Error ?? _output.Tooltip ?? Name;

    public Brush Accent => _accent;

    public IReadOnlyList<PluginBarViewModel> Bars => _bars;

    public bool HasBars => _bars.Count > 0;

    public SampleHistory? Graph => _graph;

    public bool HasGraph => _graph is { Count: > 1 } && _bars.Count == 0;

    public double GraphMaximum { get; private set; } = 1;

    public int GraphVersion => _graphVersion;

    public void Dispose()
    {
        _plugin.Updated -= OnUpdated;
        _plugin.Dispose();
    }

    /// <summary>Performs one of the plugin's actions.</summary>
    public void Execute(PluginAction action)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(action.Open))
            {
                Process.Start(new ProcessStartInfo(action.Open) { UseShellExecute = true });
            }
            else if (!string.IsNullOrWhiteSpace(action.Copy))
            {
                Clipboard.SetText(action.Copy);
            }
            else if (!string.IsNullOrWhiteSpace(action.Run) && _plugin is ScriptPlugin script)
            {
                script.RunCommand(action.Run);
            }
            else if (action.Action is "refresh")
            {
                _plugin.Refresh();
            }
            else if (!string.IsNullOrWhiteSpace(action.Action) && action.Action is not ("panel" or "menu"))
            {
                _plugin.TryHandle(action.Action);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Plugin {Id}: action failed: {ex.Message}");
        }
    }

    /// <summary>What a left click on the widget should do.</summary>
    public string ClickBehaviour()
    {
        if (_output.Click is { Action: "menu" })
        {
            return "menu";
        }

        if (_output.Click is { } click && click.Action is not "panel")
        {
            return "action";
        }

        if (_output.Panel is not null || _output.Error is not null || _output.Click is { Action: "panel" })
        {
            return "panel";
        }

        return _output.Menu.Count > 0 ? "menu" : "refresh";
    }

    private void OnUpdated(PluginUpdate update)
    {
        if (_dispatcher.CheckAccess())
        {
            Apply(update);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Apply(update));
        }
    }

    private void Apply(PluginUpdate update)
    {
        PluginOutput output = update.Output;

        // Keep showing the last good content when a refresh fails; just flag the error.
        if (output.Error is not null && !_output.Hidden && _output.Error is null)
        {
            output = _output with { Error = output.Error };
        }

        _output = output;
        _image = update.Image;

        Color accent = ParseColor(output.Color) ?? DefaultAccent;
        _accent = Freeze(new SolidColorBrush(accent));

        _bars = output.Bars
            .Select(bar => new PluginBarViewModel(bar.Label, bar.Value, Freeze(new SolidColorBrush(BarColor(bar, accent))), bar.Detail))
            .ToList();

        if (output.Graph.Count > 1)
        {
            _graph = new SampleHistory(output.Graph.Count);
            double min = output.Graph.Min();
            foreach (double value in output.Graph)
            {
                // Show the shape: lift the series so its minimum sits slightly above the floor.
                _graph.Add(value - min + 1);
            }

            GraphMaximum = Math.Max(1, _graph.Max());
            _graphVersion++;
        }
        else
        {
            _graph = null;
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Gauges turn amber at 75 % and red at 90 %, like system storage indicators.</summary>
    private static Color BarColor(PluginBar bar, Color accent)
    {
        if (ParseColor(bar.Color) is { } own)
        {
            return own;
        }

        return bar.Value switch
        {
            >= 0.9 => Color.FromRgb(0xFF, 0x5F, 0x57),
            >= 0.75 => Color.FromRgb(0xFF, 0xB4, 0x54),
            _ => accent,
        };
    }

    internal static Color? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
