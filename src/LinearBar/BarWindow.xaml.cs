using System.Windows;
using System.Windows.Controls;
using LinearBar.Core.Config;
using System.Windows.Media;
using System.Windows.Input;
using LinearBar.Core.Dock;
using LinearBar.Dock;
using LinearBar.Infrastructure;
using LinearBar.Shell;
using LinearBar.Visuals;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using static ManagedShell.Interop.NativeMethods;
using NotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
using Point = System.Windows.Point;
using Rect = ManagedShell.Interop.NativeMethods.Rect;

namespace LinearBar;

/// <summary>
/// The bar: an AppBar docked to the bottom edge of a screen.
/// </summary>
/// <remarks>
/// <para>
/// ManagedShell's <see cref="AppBarWindow"/> registers the window with the system as an
/// application desktop toolbar, which reserves the bar's height so maximised windows stop
/// above it, and keeps it positioned through resolution, DPI and work-area changes.
/// </para>
/// <para>
/// ManagedShell assumes the window is exactly the reserved rectangle. LinearBar needs it to
/// be taller: magnified dock icons grow <em>above</em> the bar. So every time the window is
/// about to be placed on the reserved rectangle, <see cref="ExtendIntoOverflow"/> grows it
/// upwards by the overflow height. The extra band is fully transparent, and Windows passes
/// mouse input through transparent pixels of a layered window, so apps underneath stay
/// clickable.
/// </para>
/// </remarks>
internal partial class BarWindow : AppBarWindow
{
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_WINDOWPOSCHANGED = 0x0047;

    /// <summary>Breathing room between the dock and the widget groups at either end.</summary>
    private const double DockSideGap = 14;

    private readonly ShellManager _shell;
    private readonly BarViewModel _viewModel;
    private readonly List<GlassPanel> _islands = [];
    private FrameworkElement? _dockSlot;
    private BackdropWindow? _backdrop;
    private MagnifyPanel? _observedPanel;
    private double _overflowHeight;
    private Rect _reservedRect;

    public BarWindow(ShellManager shell, AppBarScreen screen, BarViewModel viewModel)
        : base(
            shell.AppBarManager,
            shell.ExplorerHelper,
            shell.FullScreenHelper,
            screen,
            viewModel.Geometry.IsTop ? AppBarEdge.Top : AppBarEdge.Bottom,
            AppBarMode.Normal,
            viewModel.Geometry.BarHeight)
    {
        _shell = shell;
        _viewModel = viewModel;
        _overflowHeight = viewModel.Geometry.OverflowHeight;

        // We are the taskbar: sit on the screen edge rather than stacking inside other bars.
        RequiresScreenEdge = true;

        InitializeComponent();
        DataContext = viewModel;

        LayoutUpdated += (_, _) =>
        {
            UpdateDockSlot();
            UpdateBackdrop();
        };
        DockHost.LabelChanged += UpdateBackdrop;
        viewModel.Dock.Items.CollectionChanged += (_, _) => UpdateDockSlot();
        Loaded += (_, _) => BuildLayout();
    }

    /// <summary>True when the bar sits at the top of the screen.</summary>
    public bool IsTop => AppBarEdge == AppBarEdge.Top;

    /// <summary>The glass islands built from the layout, for diagnostics.</summary>
    public IReadOnlyList<GlassPanel> Islands => _islands;

    /// <summary>
    /// The window that blurs what is behind the glass islands, or null for no blur.
    /// Owned by the caller.
    /// </summary>
    public BackdropWindow? Backdrop
    {
        get => _backdrop;
        set
        {
            _backdrop?.Hide();
            _backdrop = value;
            UpdateBackdrop();
        }
    }

    /// <summary>The dock hosted in this window.</summary>
    public DockView DockView => DockHost;

    /// <summary>Screen rectangle reserved for the bar, in device pixels.</summary>
    public Rect ReservedRect => _reservedRect;

    /// <summary>Applies new bar and overflow heights after a settings change.</summary>
    public void ApplyGeometry()
    {
        BarGeometry geometry = _viewModel.Geometry;
        DesiredHeight = geometry.BarHeight;
        _overflowHeight = geometry.OverflowHeight;

        if (Handle != IntPtr.Zero)
        {
            UpdatePosition();
        }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            ExtendIntoOverflow(lParam, IsTop);
        }

        IntPtr result = base.WndProc(hwnd, msg, wParam, lParam, ref handled);

        if (msg == WM_WINDOWPOSCHANGED)
        {
            ReportPlacement();
            UpdateBackdrop();
        }

        return result;
    }

    /// <summary>
    /// Grows a pending move onto the reserved rectangle upwards by the overflow height.
    /// Only requests whose height is exactly the reserved height are touched, which makes
    /// this idempotent: a request that already includes the overflow is left alone.
    /// </summary>
    private void ExtendIntoOverflow(IntPtr lParam, bool downwards)
    {
        int overflow = OverflowPixels;
        if (overflow <= 0)
        {
            return;
        }

        WINDOWPOS position = WINDOWPOS.FromMessage(lParam);
        if ((position.flags & (SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOMOVE)) != 0)
        {
            return;
        }

        if (position.cy != ReservedPixels)
        {
            return;
        }

        // At the bottom the band goes above the bar; at the top, below it.
        if (!downwards)
        {
            position.y -= overflow;
        }

        position.cy += overflow;
        position.UpdateMessage(lParam);
    }

    /// <summary>
    /// Tells the tray service where the bar is, so apps asking the shell for the taskbar
    /// position (to place their tray pop-ups) get the right answer.
    /// </summary>
    private void ReportPlacement()
    {
        Rect window = WindowRect;
        if (window.Width <= 0 || window.Height <= 0)
        {
            return;
        }

        int reservedHeight = Math.Min(window.Height, ReservedPixels);
        var reserved = IsTop
            ? new Rect(window.Left, window.Top, window.Right, window.Top + reservedHeight)
            : new Rect(window.Left, window.Bottom - reservedHeight, window.Right, window.Bottom);
        if (reserved.Left == _reservedRect.Left && reserved.Top == _reservedRect.Top
            && reserved.Right == _reservedRect.Right && reserved.Bottom == _reservedRect.Bottom)
        {
            return;
        }

        _reservedRect = reserved;
        _shell.NotificationArea?.SetTrayHostSizeData(new TrayHostSizeData { edge = IsTop ? ABEdge.ABE_TOP : ABEdge.ABE_BOTTOM, rc = reserved });
        Log.Debug($"Bar placed: window {window.Left},{window.Top} {window.Width}x{window.Height}; reserved top {reserved.Top}.");
    }

    private int ReservedPixels => Convert.ToInt32(DesiredHeight * DpiScale);

    private int OverflowPixels => (int)Math.Round(_overflowHeight * DpiScale);

    protected override void OnFullScreenEnter(ManagedShell.AppBar.FullScreenApp app)
    {
        base.OnFullScreenEnter(app);
        UpdateBackdrop();
    }

    protected override void OnFullScreenLeave()
    {
        base.OnFullScreenLeave();
        UpdateBackdrop();
    }

    // ---- Backdrop blur ------------------------------------------------------------------

    /// <summary>Keeps the blur window under the bar and cut to the current island shapes.</summary>
    private void UpdateBackdrop()
    {
        if (_backdrop is null || Handle == IntPtr.Zero)
        {
            return;
        }

        // The dock panel only exists once the items control has been templated.
        if (DockHost.Panel is { } panel && !ReferenceEquals(panel, _observedPanel))
        {
            if (_observedPanel is not null)
            {
                _observedPanel.PlatterChanged -= OnPlatterChanged;
            }

            _observedPanel = panel;
            panel.PlatterChanged += OnPlatterChanged;
        }

        double dpi = DpiScale;
        var shapes = new List<(System.Windows.Rect Bounds, double Radius)>(4);
        double islandRadius = _viewModel.IslandRadius;

        if (ContinuousGlass.IsVisible)
        {
            AddShape(shapes, BoundsOf(ContinuousGlass), 0, dpi);
        }

        foreach (GlassPanel island in _islands)
        {
            if (!island.Plain)
            {
                AddShape(shapes, BoundsOf(island), islandRadius, dpi);
            }
        }

        if (_observedPanel is { ShowPlatter: true } dock && !dock.PlatterBounds.IsEmpty)
        {
            AddShape(shapes, Transform(dock, dock.PlatterBounds), islandRadius, dpi);
        }

        if (DockHost.LabelBounds(this) is { } label)
        {
            AddShape(shapes, label, 10, dpi);
        }

        Rect window = WindowRect;
        var barRect = new Int32Rect(window.Left, window.Top, window.Width, window.Height);
        bool show = IsVisible && Topmost && !IsClosing;
        _backdrop.Update(Handle, barRect, show, shapes, Glass.Smoothing);
    }

    private void OnPlatterChanged(System.Windows.Rect platter) => UpdateBackdrop();

    private System.Windows.Rect? BoundsOf(FrameworkElement element) =>
        element.IsVisible && element.ActualWidth > 0 ? Transform(element, new System.Windows.Rect(element.RenderSize)) : null;

    private System.Windows.Rect? Transform(Visual element, System.Windows.Rect bounds)
    {
        try
        {
            return element.TransformToAncestor(this).TransformBounds(bounds);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void AddShape(List<(System.Windows.Rect Bounds, double Radius)> shapes, System.Windows.Rect? bounds, double radius, double dpi)
    {
        if (bounds is not { IsEmpty: false } b || b.Width < 2 || b.Height < 2)
        {
            return;
        }

        var scaled = new System.Windows.Rect(Math.Round(b.X * dpi), Math.Round(b.Y * dpi), Math.Round(b.Width * dpi), Math.Round(b.Height * dpi));
        shapes.Add((scaled, Math.Round(radius * dpi)));
    }

    // ---- Layout -------------------------------------------------------------------------

    /// <summary>
    /// Fills the zones from the layout settings: runs of widgets become glass islands, "gap"
    /// starts a new island, and "dock" becomes a placeholder the dock is aligned to.
    /// </summary>
    public void BuildLayout()
    {
        LayoutSettings layout = _viewModel.Layout;
        double margin = Math.Max(0, _viewModel.Geometry.IslandMargin);
        bool continuous = layout.IsContinuous;

        Strip.VerticalAlignment = IsTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        ContinuousGlass.Visibility = continuous ? Visibility.Visible : Visibility.Collapsed;
        _islands.Clear();
        _dockSlot = null;

        // Plugins the layout does not mention go to their own default side, so newly
        // installed script plugins show up without editing the layout.
        var mentioned = new HashSet<string>(layout.Left.Concat(layout.Center).Concat(layout.Right), StringComparer.Ordinal);
        List<string> left = [.. layout.Left];
        List<string> right = [.. layout.Right];
        foreach (LinearBar.Plugins.PluginWidgetViewModel widget in _viewModel.Plugins.Widgets.Where(w => !mentioned.Contains(w.Id)))
        {
            if (widget.Placement == LinearBar.Core.Plugins.PluginPlacement.Left)
            {
                left.Add(widget.Id);
            }
            else
            {
                right.Insert(0, widget.Id);
            }
        }

        Fill(LeftZone, left, margin, continuous);
        Fill(CenterZone, layout.Center, margin, continuous);
        Fill(RightZone, right, margin, continuous);

        LeftZone.Margin = new Thickness(continuous ? 4 : margin, 0, 0, 0);
        RightZone.Margin = new Thickness(0, 0, continuous ? 4 : margin, 0);
        DockHost.Visibility = _dockSlot is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateDockSlot();
    }

    private void Fill(StackPanel zone, IReadOnlyList<string> items, double margin, bool continuous)
    {
        zone.Children.Clear();
        StackPanel? run = null;

        foreach (string id in items)
        {
            if (id == "gap")
            {
                run = null;
                continue;
            }

            if (id == "dock")
            {
                run = null;
                _dockSlot = new Border { Height = _viewModel.IslandHeight, Margin = new Thickness(zone.Children.Count > 0 ? margin : 0, 0, 0, 0) };
                zone.Children.Add(_dockSlot);
                continue;
            }

            if (CreateItem(id) is not { } element)
            {
                continue;
            }

            if (run is null)
            {
                run = new StackPanel { Orientation = Orientation.Horizontal };
                var island = new GlassPanel
                {
                    Height = _viewModel.IslandHeight,
                    Radius = _viewModel.IslandRadius,
                    Padding = new Thickness(3),
                    Plain = continuous,
                    Margin = new Thickness(zone.Children.Count > 0 ? Math.Max(4, margin) : 0, 0, 0, 0),
                    Child = run,
                };
                _islands.Add(island);
                zone.Children.Add(island);
            }

            run.Children.Add(element);
        }
    }

    private FrameworkElement? CreateItem(string id)
    {
        (object? content, string? template) = id switch
        {
            "start" => ((object?)_viewModel, "StartTemplate"),
            "battery" => (_viewModel, "BatteryTemplate"),
            "tray" => (_viewModel, "TrayTemplate"),
            "clock" => (_viewModel, "ClockTemplate"),
            "cpu" or "memory" or "network" => (_viewModel.Metrics.Items.FirstOrDefault(m => m.Key == id), "MetricTemplate"),
            _ => (_viewModel.Plugins.Widgets.FirstOrDefault(w => w.Id == id), "PluginTemplate"),
        };

        if (content is null)
        {
            return null;
        }

        return new ContentControl
        {
            Content = content,
            ContentTemplate = (DataTemplate)FindResource(template),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// Sizes the dock placeholder (the dock's natural width, or whatever space is left) and
    /// tells the dock to fit exactly into it.
    /// </summary>
    private void UpdateDockSlot()
    {
        if (_dockSlot is null || ActualWidth <= 0)
        {
            return;
        }

        DockViewModel dock = _viewModel.Dock;
        double lensMargin = new LinearBar.Core.Dock.Lens(dock.MaxScale - 1, dock.LensRadius).MaxDisplacement;
        double pad = LinearBar.Core.Dock.BarGeometry.PlatterSidePadding;
        double natural = dock.Items.Count * dock.SlotWidth + 2 * (lensMargin + pad);

        double others = 0;
        foreach (StackPanel zone in new[] { LeftZone, CenterZone, RightZone })
        {
            others += zone.Margin.Left + zone.Margin.Right;
            foreach (FrameworkElement child in zone.Children)
            {
                others += child.Margin.Left + child.Margin.Right + (ReferenceEquals(child, _dockSlot) ? 0 : child.ActualWidth);
            }
        }

        double width = Math.Max(0, Math.Min(natural, ActualWidth - others - 8));
        if (Math.Abs(_dockSlot.Width - width) > 0.5 || double.IsNaN(_dockSlot.Width))
        {
            _dockSlot.Width = width;
            return; // the next layout pass positions it
        }

        if (Transform(_dockSlot, new System.Windows.Rect(_dockSlot.RenderSize)) is { } slot)
        {
            DockHost.SetInsets(slot.Left + pad, Math.Max(0, ActualWidth - slot.Right) + pad);
        }
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs e) => CustomizeRequested?.Invoke();

    /// <summary>Raised when the user asks to customise the bar.</summary>
    public event Action? CustomizeRequested;

    // ---- Plugin widgets -----------------------------------------------------------------

    private void OnPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LinearBar.Plugins.PluginWidgetViewModel widget } element)
        {
            return;
        }

        switch (widget.ClickBehaviour())
        {
            case "panel":
                LinearBar.Plugins.PluginPanelPopup.Show(widget, element);
                break;
            case "menu":
                OpenPluginMenu(widget, element);
                break;
            case "action":
                widget.Execute(widget.Output.Click!);
                break;
            default:
                widget.Plugin.Refresh();
                break;
        }
    }

    private void OnPluginRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LinearBar.Plugins.PluginWidgetViewModel widget } element)
        {
            e.Handled = true;
            OpenPluginMenu(widget, element);
        }
    }

    private static void OpenPluginMenu(LinearBar.Plugins.PluginWidgetViewModel widget, FrameworkElement anchor)
    {
        System.Windows.Controls.ContextMenu menu = LinearBar.Plugins.PluginPanelPopup.BuildMenu(widget);
        menu.PlacementTarget = anchor;
        menu.Placement = PopupPlacement.Menu;
        menu.IsOpen = true;
    }

    // ---- Start button -------------------------------------------------------------------

    private void OnStartRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel.StartMenuCommand.Execute(null);
    }

    // ---- Notification-area icons ---------------------------------------------------------
    // The icons belong to other apps. ManagedShell delivers our mouse events to them as the
    // window messages they expect from a taskbar.

    private void OnTrayIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TrayIconOf(sender, out FrameworkElement? element) is { } icon)
        {
            e.Handled = true;
            UpdatePlacement(element!, icon);
            icon.IconMouseDown(e.ChangedButton, MouseHelper.GetCursorPositionParam(), DoubleClickTime);
        }
    }

    private void OnTrayIconMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (TrayIconOf(sender, out _) is { } icon)
        {
            e.Handled = true;
            icon.IconMouseUp(e.ChangedButton, MouseHelper.GetCursorPositionParam(), DoubleClickTime);
        }
    }

    private void OnTrayIconMouseEnter(object sender, MouseEventArgs e)
    {
        if (TrayIconOf(sender, out FrameworkElement? element) is { } icon)
        {
            UpdatePlacement(element!, icon);
            icon.IconMouseEnter(MouseHelper.GetCursorPositionParam());
        }
    }

    private void OnTrayIconMouseLeave(object sender, MouseEventArgs e)
    {
        TrayIconOf(sender, out _)?.IconMouseLeave(MouseHelper.GetCursorPositionParam());
    }

    private void OnTrayIconMouseMove(object sender, MouseEventArgs e)
    {
        TrayIconOf(sender, out _)?.IconMouseMove(MouseHelper.GetCursorPositionParam());
    }

    private static int DoubleClickTime => (int)Native.GetDoubleClickTime();

    private static NotifyIcon? TrayIconOf(object sender, out FrameworkElement? element)
    {
        element = sender as FrameworkElement;
        return element?.DataContext as NotifyIcon;
    }

    /// <summary>Records where the icon is on screen; apps query it to anchor their fly-outs.</summary>
    private static void UpdatePlacement(FrameworkElement element, NotifyIcon icon)
    {
        try
        {
            Point topLeft = element.PointToScreen(new Point(0, 0));
            Point bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            icon.Placement = new Rect((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
        }
        catch (InvalidOperationException)
        {
            // The element is not connected to a window (yet); nothing to report.
        }
    }
}
