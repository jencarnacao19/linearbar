using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LinearBar.Infrastructure;

namespace LinearBar.Dock;

/// <summary>The dock: an items control laid out by <see cref="MagnifyPanel"/> plus the name label.</summary>
internal partial class DockView : UserControl
{
    /// <summary>Gap between the top of the hovered icon and its label.</summary>
    private const double LabelGap = 7;

    private MagnifyPanel? _panel;
    private double _leftInset;
    private double _rightInset;

    public DockView()
    {
        InitializeComponent();
    }

    /// <summary>The panel doing the layout, once the items control has created it.</summary>
    public MagnifyPanel? Panel => _panel;

    /// <summary>Tells the dock how much of each end of the bar is taken by other widgets.</summary>
    public void SetInsets(double left, double right)
    {
        _leftInset = left;
        _rightInset = right;

        if (_panel is not null)
        {
            _panel.LeftInset = left;
            _panel.RightInset = right;
        }
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_panel, sender))
        {
            return;
        }

        if (_panel is not null)
        {
            _panel.HoverChanged -= OnHoverChanged;
        }

        _panel = (MagnifyPanel)sender;
        _panel.LeftInset = _leftInset;
        _panel.RightInset = _rightInset;
        _panel.HoverChanged += OnHoverChanged;
    }

    /// <summary>Places the name label above the icon under the cursor.</summary>
    private void OnHoverChanged(DockHover hover)
    {
        if (DataContext is not DockViewModel dock
            || !dock.ShowLabels
            || hover.Index < 0
            || hover.Index >= dock.Items.Count
            || string.IsNullOrWhiteSpace(dock.Items[hover.Index].DisplayName))
        {
            Label.Visibility = Visibility.Collapsed;
            return;
        }

        LabelText.Text = dock.Items[hover.Index].DisplayName;
        Label.Visibility = Visibility.Visible;

        // Measure now so the label is centred with its new width in this very frame.
        Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Label.DesiredSize;

        double x = hover.CenterX - size.Width / 2;
        x = Math.Clamp(x, 4, Math.Max(4, ActualWidth - size.Width - 4));
        double y = Math.Max(0, hover.Top - LabelGap - size.Height);

        LabelOffset.X = Math.Round(x);
        LabelOffset.Y = Math.Round(y);
    }

    private void OnItemPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DockItemViewModel item })
        {
            item.NotePressed();
        }
    }

    /// <summary>Middle click starts a new instance, as on the Windows taskbar.</summary>
    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: DockItemViewModel item })
        {
            e.Handled = true;
            Run(item.Launch);
        }
    }

    private void OnItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItemViewModel item } element)
        {
            return;
        }

        ContextMenu menu = element.ContextMenu ?? new ContextMenu();
        FillMenu(menu, item);

        if (menu.Items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        element.ContextMenu = menu;
    }

    /// <summary>Rebuilds <paramref name="menu"/> for <paramref name="item"/>.</summary>
    internal static void FillMenu(ContextMenu menu, DockItemViewModel item)
    {
        menu.Items.Clear();

        foreach (DockMenuEntry entry in item.BuildMenu())
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            Action action = entry.Action!;
            var menuItem = new MenuItem { Header = entry.Header };
            menuItem.Click += (_, _) => Run(action);
            menu.Items.Add(menuItem);
        }
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Dock action failed", ex);
        }
    }
}
