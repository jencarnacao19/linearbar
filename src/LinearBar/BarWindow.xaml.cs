using System.Windows;
using System.Windows.Input;
using LinearBar.Core.Dock;
using LinearBar.Dock;
using LinearBar.Infrastructure;
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

    // ---- Layout -------------------------------------------------------------------------

    /// <summary>Keeps the dock clear of the widget groups, whatever their width.</summary>
    private void OnSideGroupSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double left = LeftGroup.ActualWidth + LeftGroup.Margin.Left;
        double right = RightGroup.ActualWidth + RightGroup.Margin.Right;

        DockHost.SetInsets(left + DockSideGap, right + DockSideGap);
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
