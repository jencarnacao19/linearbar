using System.Windows;
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
            AppBarEdge.Bottom,
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

        LayoutUpdated += (_, _) => UpdateBackdrop();
        DockHost.LabelChanged += UpdateBackdrop;
    }

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
            ExtendIntoOverflow(lParam);
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
    private void ExtendIntoOverflow(IntPtr lParam)
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

        position.y -= overflow;
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

        var reserved = new Rect(window.Left, window.Bottom - Math.Min(window.Height, ReservedPixels), window.Right, window.Bottom);
        if (reserved.Left == _reservedRect.Left && reserved.Top == _reservedRect.Top
            && reserved.Right == _reservedRect.Right && reserved.Bottom == _reservedRect.Bottom)
        {
            return;
        }

        _reservedRect = reserved;
        _shell.NotificationArea?.SetTrayHostSizeData(new TrayHostSizeData { edge = ABEdge.ABE_BOTTOM, rc = reserved });
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

        AddShape(shapes, BoundsOf(LeftGroup), islandRadius, dpi);
        AddShape(shapes, BoundsOf(RightGroup), islandRadius, dpi);

        if (_observedPanel is { } dock && !dock.PlatterBounds.IsEmpty)
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

    /// <summary>Keeps the dock clear of the widget groups, whatever their width.</summary>
    private void OnSideGroupSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double left = LeftGroup.ActualWidth + LeftGroup.Margin.Left;
        double right = RightGroup.ActualWidth + RightGroup.Margin.Right;

        DockHost.SetInsets(left + DockSideGap, right + DockSideGap);
    }

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
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
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
