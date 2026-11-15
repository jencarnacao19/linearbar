using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;
using LinearBar.Visuals;

namespace LinearBar.Plugins;

/// <summary>
/// The pop-up a plugin widget opens: a squircle of opaque glass above the bar with the
/// plugin's title, rows (with gauges) and buttons. It rebuilds itself while open whenever
/// the plugin publishes new content.
/// </summary>
internal sealed class PluginPanelPopup
{
    private const double PanelWidth = 312;

    private readonly PluginWidgetViewModel _widget;
    private readonly Popup _popup;
    private readonly GlassPanel _surface;

    private PluginPanelPopup(PluginWidgetViewModel widget, FrameworkElement anchor)
    {
        _widget = widget;
        _surface = new GlassPanel { Solid = true, Radius = 22, Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(8) };

        // A pop-up is its own visual tree: give it the bar's text style.
        TextElement.SetForeground(_surface, (Brush)Application.Current.FindResource("TextBrush"));
        TextElement.SetFontFamily(_surface, (FontFamily)Application.Current.FindResource("UiFont"));
        _popup = new Popup
        {
            Child = _surface,
            PlacementTarget = anchor,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceAbove,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        _popup.Closed += (_, _) => widget.PropertyChanged -= OnWidgetChanged;
        widget.PropertyChanged += OnWidgetChanged;
        Build();
    }

    /// <summary>Opens the panel of <paramref name="widget"/> above <paramref name="anchor"/>.</summary>
    public static PluginPanelPopup Show(PluginWidgetViewModel widget, FrameworkElement anchor)
    {
        var panel = new PluginPanelPopup(widget, anchor);
        panel._popup.IsOpen = true;
        return panel;
    }

    public void Close() => _popup.IsOpen = false;

    /// <summary>The right-click menu of a widget: the plugin's own entries plus refresh.</summary>
    public static ContextMenu BuildMenu(PluginWidgetViewModel widget)
    {
        var menu = new ContextMenu();
        foreach (PluginAction action in widget.Output.Menu)
        {
            PluginAction captured = action;
            var item = new MenuItem { Header = action.Title ?? action.Action ?? action.Open };
            item.Click += (_, _) => widget.Execute(captured);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var refresh = new MenuItem { Header = "Atualizar " + widget.Name };
        refresh.Click += (_, _) => widget.Plugin.Refresh();
        menu.Items.Add(refresh);

        var folder = new MenuItem { Header = "Abrir pasta de plugins" };
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(PluginHost.UserPluginsFolder);
            Process.Start(new ProcessStartInfo(PluginHost.UserPluginsFolder) { UseShellExecute = true });
        };
        menu.Items.Add(folder);
        return menu;
    }

    private static CustomPopupPlacement[] PlaceAbove(Size popupSize, Size targetSize, Point offset) =>
        [new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 2), PopupPrimaryAxis.Horizontal)];

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e) => Build();

    private void Build()
    {
        PluginOutput output = _widget.Output;
        PluginPanelContent? panel = output.Panel is { } p ? new PluginPanelContent(p) : null;

        var root = new StackPanel { Width = PanelWidth - 32 };
        root.Children.Add(Header(panel));

        if (output.Error is { } error)
        {
            root.Children.Add(new TextBlock
            {
                Text = error,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = (Brush)Application.Current.FindResource("AttentionBrush"),
            });
        }

        if (panel is not null)
        {
            foreach (PluginRow row in panel.Panel.Rows)
            {
                root.Children.Add(Row(row));
            }
        }

        IReadOnlyList<PluginAction> buttons = panel?.Panel.Buttons is { Count: > 0 } b ? b : output.Menu;
        if (buttons.Count > 0)
        {
            var bar = new WrapPanel { Margin = new Thickness(-3, 12, -3, -3), HorizontalAlignment = HorizontalAlignment.Center };
            foreach (PluginAction action in buttons)
            {
                bar.Children.Add(Button(action));
            }

            root.Children.Add(bar);
        }

        _surface.Child = root;
    }

    private UIElement Header(PluginPanelContent? panel)
    {
        string title = panel?.Panel.Title ?? _widget.Name;
        string? subtitle = panel?.Panel.Subtitle;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Album art (or another image) gets a large squircle; otherwise the plugin icon on a tile.
        FrameworkElement art;
        if (_widget.Image is { } image)
        {
            art = new SquircleBorder
            {
                Width = 72,
                Height = 72,
                Radius = 16,
                ClipContent = true,
                Child = new Image { Source = image, Stretch = Stretch.UniformToFill },
            };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
        }
        else
        {
            art = new SquircleBorder
            {
                Width = 40,
                Height = 40,
                Radius = 11,
                Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                Child = new IconView { Width = 22, Height = 22, Icon = _widget.Icon, Brush = _widget.Accent },
            };
        }

        art.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(art);

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("SubtleTextBrush"),
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private UIElement Row(PluginRow row)
    {
        Brush accent = PluginWidgetViewModel.ParseColor(row.Color) is { } c ? Frozen(c) : _widget.Accent;

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var icon = new IconView { Width = 16, Height = 16, Icon = row.Icon, Brush = accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        grid.Children.Add(icon);

        var labels = new StackPanel();
        labels.Children.Add(new TextBlock { Text = row.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrWhiteSpace(row.Detail))
        {
            labels.Children.Add(new TextBlock { Text = row.Detail, FontSize = 11, Foreground = (Brush)Application.Current.FindResource("SubtleTextBrush") });
        }

        Grid.SetColumn(labels, 1);
        grid.Children.Add(labels);

        if (!string.IsNullOrWhiteSpace(row.Value))
        {
            var value = new TextBlock { Text = row.Value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
        }

        if (row.Progress is { } progress)
        {
            var gauge = new PillGauge { Height = 5, Value = progress, Fill = GaugeBrush(progress, accent), Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetRow(gauge, 1);
            Grid.SetColumn(gauge, 1);
            Grid.SetColumnSpan(gauge, 2);
            grid.Children.Add(gauge);
        }

        return grid;
    }

    private UIElement Button(PluginAction action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrWhiteSpace(action.Icon))
        {
            content.Children.Add(new IconView { Width = 15, Height = 15, Icon = action.Icon, Brush = (Brush)Application.Current.FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center });
        }

        bool iconOnly = !string.IsNullOrWhiteSpace(action.Icon) && action.Action?.StartsWith("media.", StringComparison.Ordinal) == true;
        if (!string.IsNullOrWhiteSpace(action.Title) && !iconOnly)
        {
            content.Children.Add(new TextBlock { Text = action.Title, Margin = new Thickness(content.Children.Count > 0 ? 7 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        var button = new Button { Content = content, Style = (Style)Application.Current.FindResource("ChipButton"), ToolTip = action.Title };
        button.Click += (_, _) =>
        {
            _widget.Execute(action);

            // Opening something elsewhere ends the interaction; media buttons keep the panel.
            if (!string.IsNullOrWhiteSpace(action.Open) || !string.IsNullOrWhiteSpace(action.Copy))
            {
                _popup.IsOpen = false;
            }
        };

        return button;
    }

    private static Brush GaugeBrush(double value, Brush accent) => value switch
    {
        >= 0.9 => Frozen(Color.FromRgb(0xFF, 0x5F, 0x57)),
        >= 0.75 => Frozen(Color.FromRgb(0xFF, 0xB4, 0x54)),
        _ => accent,
    };

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record PluginPanelContent(PluginPanel Panel);
}
