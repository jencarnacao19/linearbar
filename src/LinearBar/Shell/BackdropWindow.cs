using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LinearBar.Core.Geometry;
using LinearBar.Infrastructure;

namespace LinearBar.Shell;

/// <summary>
/// A plain Win32 window placed just below the bar that shows a blurred copy of whatever is
/// behind it — the frosted part of the glass — clipped to the shape of the glass islands.
/// </summary>
/// <remarks>
/// <para>
/// The blur cannot live on the bar window itself: that window extends into a transparent,
/// click-through band above the bar for the magnified icons, and the system blur applies
/// to the whole window. So the blur gets its own window, cut to the islands with a window
/// region (polygons from <see cref="Squircle"/>), kept directly under the bar in the
/// z-order and never hit-testable. The bar paints the tint, sheen and rim on top.
/// </para>
/// <para>
/// The region is inset by a pixel: region edges are not anti-aliased, and the inset keeps
/// them under the bar's anti-aliased rim.
/// </para>
/// </remarks>
internal sealed class BackdropWindow : IDisposable
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;

    private const int RGN_OR = 2;
    private const int WINDING = 2;

    private readonly HwndSource _source;
    private List<(Rect Bounds, double Radius)> _shapes = [];
    private double _smoothing = Squircle.AppleSmoothing;
    private bool _visible;

    public BackdropWindow()
    {
        var parameters = new HwndSourceParameters("LinearBar.Backdrop")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = 1,
            UsesPerPixelOpacity = false,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        IsBlurEnabled = EnableBlur(_source.Handle);
        Log.Info(IsBlurEnabled ? "Backdrop blur enabled." : "Backdrop blur is not available on this system.");
    }

    /// <summary>False when the system refused the blur; the glass is then drawn without it.</summary>
    public bool IsBlurEnabled { get; }

    public IntPtr Handle => _source.Handle;

    /// <summary>
    /// Places the backdrop exactly under <paramref name="barHandle"/>, covering
    /// <paramref name="barRect"/> (device pixels), and clips it to <paramref name="shapes"/>
    /// (device pixels relative to the bar window).
    /// </summary>
    public void Update(IntPtr barHandle, Int32Rect barRect, bool show, IReadOnlyList<(Rect Bounds, double Radius)> shapes, double smoothing)
    {
        if (!show || shapes.Count == 0 || barRect.Width <= 0 || barRect.Height <= 0)
        {
            Hide();
            return;
        }

        if (!SameShapes(shapes) || smoothing != _smoothing)
        {
            _shapes = [.. shapes];
            _smoothing = smoothing;
            ApplyRegion();
        }

        // Inserting "after" the bar puts the backdrop directly below it, and makes it
        // topmost too since the bar is.
        SetWindowPos(Handle, barHandle, barRect.X, barRect.Y, barRect.Width, barRect.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _visible = true;
    }

    public void Hide()
    {
        if (_visible)
        {
            ShowWindow(Handle, SW_HIDE);
            _visible = false;
        }
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private bool SameShapes(IReadOnlyList<(Rect Bounds, double Radius)> shapes)
    {
        if (shapes.Count != _shapes.Count)
        {
            return false;
        }

        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i] != _shapes[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyRegion()
    {
        IntPtr combined = CreateRectRgn(0, 0, 0, 0);
        foreach ((Rect bounds, double radius) in _shapes)
        {
            if (bounds.Width < 3 || bounds.Height < 3)
            {
                continue;
            }

            List<Vec> outline = Squircle
                .Build(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2, Math.Max(0, radius - 1), _smoothing)
                .Flatten(10);

            var points = new POINT[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                points[i] = new POINT { X = (int)Math.Round(outline[i].X), Y = (int)Math.Round(outline[i].Y) };
            }

            IntPtr polygon = CreatePolygonRgn(points, points.Length, WINDING);
            CombineRgn(combined, combined, polygon, RGN_OR);
            DeleteObject(polygon);
        }

        // The system owns the region from here on; do not delete it.
        SetWindowRgn(Handle, combined, true);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                handled = true;
                return new IntPtr(HTTRANSPARENT);

            case WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);

            case WM_ERASEBKGND:
                // Black is written with zero alpha by GDI, which is what lets the blur show.
                GetClientRect(hwnd, out RECT client);
                FillRect(wParam, ref client, GetStockObject(BLACK_BRUSH));
                handled = true;
                return new IntPtr(1);

            case WM_PAINT:
                IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT paint);
                FillRect(hdc, ref paint.rcPaint, GetStockObject(BLACK_BRUSH));
                EndPaint(hwnd, ref paint);
                handled = true;
                return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static bool EnableBlur(IntPtr hwnd)
    {
        var accent = new AccentPolicy { AccentState = ACCENT_ENABLE_BLURBEHIND };
        int size = Marshal.SizeOf(accent);
        IntPtr pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = pointer,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    // ---- Win32 ---------------------------------------------------------------------------

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int BLACK_BRUSH = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePolygonRgn(POINT[] points, int count, int fillMode);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
