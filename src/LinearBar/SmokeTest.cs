using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;
using LinearBar.Dock;
using LinearBar.Infrastructure;
using LinearBar.Shell;
using Point = System.Windows.Point;

namespace LinearBar;

/// <summary>
/// End-to-end check run with <c>--smoke-test</c>: starts the real bar, verifies what can be
/// verified without a human (placement, reserved screen space, metrics, dock magnification,
/// pop-up templates), saves screenshots, writes a report to the log and exits with 0 on
/// success. CI runs it on a real Windows machine, which is the only place the Win32 side
/// of LinearBar can be exercised automatically.
/// </summary>
internal sealed class SmokeTest(ShellHost host, StartupOptions options, Action<int> exit)
{
    private int _failures;

    public void Start() => _ = RunAsync();

    private async Task RunAsync()
    {
        bool passed = false;
        try
        {
            // Let the icons load and a few metric samples arrive.
            await Task.Delay(3500);

            ReportEnvironment();
            CheckWindow();
            CheckMetrics();
            ReportDock();
            ReportTray();
            await CheckMagnificationAsync();
            CheckPopups();
            SaveScreenshots();

            Check("no errors logged", Log.ErrorCount == 0, $"{Log.ErrorCount} error(s), {Log.ShellErrorCount} from ManagedShell");
            passed = _failures == 0;
        }
        catch (Exception ex)
        {
            Log.Error("SMOKE: aborted by an exception", ex);
        }
        finally
        {
            Log.Info($"SMOKE RESULT: {(passed ? "PASS" : "FAIL")} ({_failures} failed check(s))");
            exit(passed ? 0 : 1);
        }
    }

    private void Check(string name, bool ok, string detail)
    {
        if (!ok)
        {
            _failures++;
        }

        Log.Info($"SMOKE CHECK [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
    }

    private void ReportEnvironment()
    {
        BarWindow window = host.Window;
        Log.Info($"SMOKE: OS {Environment.OSVersion.VersionString}, 64-bit process {Environment.Is64BitProcess}, .NET {Environment.Version}");
        Log.Info($"SMOKE: screen {window.Screen.DeviceName} bounds {window.Screen.Bounds}, DPI scale {window.DpiScale:0.##}");
        Log.Info($"SMOKE: running as shell {ManagedShell.Common.Helpers.EnvironmentHelper.IsAppRunningAsShell}, Windows taskbar hidden {host.Taskbar.IsHidden}");
        Log.Info($"SMOKE: glass blur {(host.Backdrop is null ? "off" : "on")}, icon tiles {host.Dock.IconTiles}");
        if (host.ViewModel.Geometry.BarHeight > 0 && options.SettingsPath is not null)
        {
            Check("backdrop blur is active", host.Backdrop is { IsBlurEnabled: true }, host.Backdrop is null ? "no backdrop window" : "blur enabled");
        }
    }

    private void CheckWindow()
    {
        BarWindow window = host.Window;
        BarGeometry geometry = host.ViewModel.Geometry;
        var rect = window.WindowRect;
        var screen = window.Screen.Bounds;
        double dpi = window.DpiScale;

        Log.Info($"SMOKE: window handle 0x{window.Handle.ToInt64():X}, rect {rect.Left},{rect.Top} {rect.Width}x{rect.Height}, "
            + $"visible {window.IsVisible}, topmost {window.Topmost}, layout {window.ActualWidth:0.#}x{window.ActualHeight:0.#}");

        int reserved = Convert.ToInt32(geometry.BarHeight * dpi);
        int overflow = (int)Math.Round(geometry.OverflowHeight * dpi);

        Check("window is visible", window.IsVisible && window.Handle != IntPtr.Zero, $"visible {window.IsVisible}");
        Check("window spans the screen width", rect.Left == screen.Left && rect.Width == screen.Width, $"{rect.Left}+{rect.Width} vs {screen.Left}+{screen.Width}");
        Check("window sits on the bottom edge", rect.Bottom == screen.Bottom, $"bottom {rect.Bottom} vs {screen.Bottom}");
        Check("window includes the overflow band", Math.Abs(rect.Height - (reserved + overflow)) <= 1, $"height {rect.Height}, expected {reserved} + {overflow}");

        // The AppBar registration must shrink the work area by the bar height, and only by that.
        Rect workArea = SystemParameters.WorkArea;
        double expectedBottom = SystemParameters.PrimaryScreenHeight - geometry.BarHeight;
        Check("screen space is reserved for the bar only", Math.Abs(workArea.Bottom - expectedBottom) <= 2,
            $"work area bottom {workArea.Bottom:0.#}, expected {expectedBottom:0.#} (screen {SystemParameters.PrimaryScreenHeight:0.#})");
    }

    private void CheckMetrics()
    {
        var metrics = host.Metrics;
        var latest = metrics.Latest;

        Log.Info($"SMOKE: metrics samples {metrics.SampleCount}, graphs {metrics.Items.Count}, battery shown {metrics.ShowBattery}");
        if (latest is not null)
        {
            Log.Info($"SMOKE: cpu {latest.CpuPercent:0.#} %, memory {latest.MemoryUsedBytes / 1048576} of {latest.MemoryTotalBytes / 1048576} MB, "
                + $"net down {latest.NetworkDownBytesPerSecond:0} B/s up {latest.NetworkUpBytesPerSecond:0} B/s, battery {latest.BatteryPercent?.ToString() ?? "none"}");
        }

        Check("metrics are being sampled", metrics.SampleCount >= 2 && latest is not null, $"{metrics.SampleCount} sample(s)");
        Check("memory reading is plausible", latest is { MemoryTotalBytes: > 0 } && latest.MemoryUsedBytes > 0 && latest.MemoryUsedBytes <= latest.MemoryTotalBytes,
            latest is null ? "no reading" : $"{latest.MemoryPercent:0.#} % used");
        Check("cpu reading is in range", latest is { CpuPercent: >= 0 and <= 100 }, latest is null ? "no reading" : $"{latest.CpuPercent:0.#} %");

        foreach (var item in metrics.Items)
        {
            Log.Info($"SMOKE: graph {item.Label}: '{item.ValueText}', {item.History.Count} point(s), max {item.Maximum:0.#}");
        }
    }

    private void ReportDock()
    {
        int taskWindows = 0;
        foreach (object _ in host.Shell.Tasks.GroupedWindows)
        {
            taskWindows++;
        }

        Log.Info($"SMOKE: {taskWindows} task window(s), {host.Dock.Items.Count} dock item(s)");
        foreach (DockItemViewModel item in host.Dock.Items)
        {
            string icon = item.Icon is BitmapSource bitmap ? $"{bitmap.PixelWidth}x{bitmap.PixelHeight}" : item.Icon is null ? "none" : "vector";
            Log.Info($"SMOKE:   '{item.DisplayName}' pinned {item.IsPinned}, windows {item.Windows.Count}, active {item.IsActive}, icon {icon}, key {item.Key}");
        }

        Check("dock has items", host.Dock.Items.Count > 0, $"{host.Dock.Items.Count} item(s)");
    }

    private void ReportTray()
    {
        var tray = host.Shell.NotificationArea;
        if (tray is null)
        {
            Log.Info("SMOKE: tray service disabled");
            return;
        }

        Log.Info($"SMOKE: tray failed {tray.IsFailed}, {tray.TrayIcons.Count} icon(s)");
        foreach (var icon in tray.TrayIcons)
        {
            Log.Info($"SMOKE:   tray '{icon.Title}' pinned {icon.IsPinned}, hidden {icon.IsHidden}, has image {icon.Icon is not null}");
        }
    }

    /// <summary>
    /// Hovers a dock icon and checks that the lens opens around it. Tries the real pointer
    /// first, which also proves that the layered window receives input over the dock; if
    /// the session delivers no pointer input, drives the panel directly instead.
    /// </summary>
    private async Task CheckMagnificationAsync()
    {
        MagnifyPanel? panel = host.Window.DockView.Panel;
        int count = host.Dock.Items.Count;
        if (panel is null || count == 0)
        {
            Check("dock panel exists", panel is not null && count > 0, $"panel {(panel is null ? "missing" : "present")}, {count} item(s)");
            return;
        }

        double maxScale = host.ViewModel.Geometry.MaxScale;
        if (maxScale <= 1)
        {
            Log.Info("SMOKE: magnification is switched off in the settings; skipping the lens check");
            return;
        }

        int target = count / 2;
        IReadOnlyList<SlotPlacement> resting = panel.Slots;
        if (target >= resting.Count)
        {
            Check("dock is laid out", false, $"{resting.Count} slot(s) for {count} item(s)");
            return;
        }

        double x = resting[target].Center;
        double y = panel.ActualHeight - panel.BottomMargin - panel.CellHeight / 2;
        Point screen = panel.PointToScreen(new Point(x, y));

        Native.GetCursorPos(out Native.POINT original);
        Native.SetCursorPos((int)screen.X, (int)screen.Y);
        await Task.Delay(900);

        bool realInput = panel.Amount > 0.5;
        Log.Info($"SMOKE: pointer moved to {screen.X:0},{screen.Y:0}; amount {panel.Amount:0.##}, mouse over dock {panel.IsMouseOver}");

        if (realInput)
        {
            // Climb into the transparent band, onto the magnified part of the icon: the dock
            // must keep tracking the pointer there.
            double lift = panel.CellHeight * (maxScale - 1) * 0.6;
            Native.SetCursorPos((int)screen.X, (int)(screen.Y - (panel.CellHeight / 2 + lift) * host.Window.DpiScale));
            await Task.Delay(500);
            Check("dock keeps the pointer above the bar strip", panel.IsMouseOver && panel.Amount > 0.9,
                $"amount {panel.Amount:0.##}, mouse over {panel.IsMouseOver}");
        }
        else
        {
            Log.Warn("SMOKE: no pointer input reached the dock (non-interactive session?); simulating the hover instead");
            panel.SimulateHover(x);
            await Task.Delay(900);
        }

        IReadOnlyList<SlotPlacement> slots = panel.Slots;
        double hoveredScale = slots[target].Scale;
        Log.Info("SMOKE: slot scales " + string.Join(" ", slots.Select(slot => slot.Scale.ToString("0.00"))));
        Log.Info($"SMOKE: hover index {panel.Hover.Index}, label '{(panel.Hover.Index >= 0 ? host.Dock.Items[panel.Hover.Index].DisplayName : "")}'");

        Check("lens opens fully", panel.Amount > 0.95, $"amount {panel.Amount:0.###} ({(realInput ? "real pointer" : "simulated")})");
        Check("hovered icon is magnified", hoveredScale > 1 + (maxScale - 1) * 0.8, $"scale {hoveredScale:0.###} of max {maxScale:0.##}");
        Check("hovered slot is the one under the pointer", panel.Hover.Index == target, $"index {panel.Hover.Index}, expected {target}");
        if (count >= 3 && target > 0)
        {
            Check("neighbours are magnified less", slots[target - 1].Scale > 1 && slots[target - 1].Scale < hoveredScale,
                $"neighbour {slots[target - 1].Scale:0.###}");
        }

        // Capture while magnified, then let the dock settle again.
        SaveScreenshots("magnified");

        panel.SimulateHover(null);
        Native.SetCursorPos(original.X, original.Y);
        await Task.Delay(700);
        Check("lens closes again", panel.Amount < 0.05, $"amount {panel.Amount:0.###}");
    }

    /// <summary>
    /// Control templates are only parsed when first used, so open the pop-ups once: a broken
    /// template would otherwise surface in front of the user.
    /// </summary>
    private void CheckPopups()
    {
        try
        {
            if (host.Dock.Items.Count > 0)
            {
                var menu = new ContextMenu { PlacementTarget = host.Window, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
                DockView.FillMenu(menu, host.Dock.Items[0]);
                menu.IsOpen = true;
                menu.UpdateLayout();
                Log.Info($"SMOKE: dock menu opened with {menu.Items.Count} entry(ies), size {menu.ActualWidth:0}x{menu.ActualHeight:0}");
                Check("dock context menu renders", menu.Items.Count > 0 && menu.ActualHeight > 0, $"{menu.Items.Count} entry(ies)");
                menu.IsOpen = false;
            }

            var tip = new ToolTip { Content = "LinearBar", PlacementTarget = host.Window, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            tip.IsOpen = true;
            tip.UpdateLayout();
            Check("tooltip renders", tip.ActualHeight > 0, $"size {tip.ActualWidth:0}x{tip.ActualHeight:0}");
            tip.IsOpen = false;
        }
        catch (Exception ex)
        {
            Check("pop-up templates load", false, ex.GetType().Name + ": " + ex.Message);
            Log.Error("SMOKE: pop-up check failed", ex);
        }
    }

    private void SaveScreenshots(string suffix = "resting")
    {
        if (string.IsNullOrWhiteSpace(options.ScreenshotDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(options.ScreenshotDirectory);
            BarWindow window = host.Window;

            // 1. The window's own content, rendered by WPF: works even without a desktop.
            if (window.Content is FrameworkElement root && root.ActualWidth > 0 && root.ActualHeight > 0)
            {
                double dpi = window.DpiScale;
                var render = new RenderTargetBitmap(
                    (int)Math.Ceiling(root.ActualWidth * dpi),
                    (int)Math.Ceiling(root.ActualHeight * dpi),
                    96 * dpi,
                    96 * dpi,
                    PixelFormats.Pbgra32);
                render.Render(root);
                SavePng(render, Path.Combine(options.ScreenshotDirectory, $"bar-{suffix}.png"));
            }

            // 2. What is actually on screen around the bar.
            var rect = window.WindowRect;
            int margin = (int)(60 * window.DpiScale);
            if (CaptureScreen(rect.Left, rect.Top - margin, rect.Width, rect.Height + margin) is { } screen)
            {
                SavePng(screen, Path.Combine(options.ScreenshotDirectory, $"screen-{suffix}.png"));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("SMOKE: could not save screenshots: " + ex.Message);
        }
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        Log.Info($"SMOKE: saved {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
    }

    private static BitmapSource? CaptureScreen(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memoryDc = Native.CreateCompatibleDC(screenDc);
        IntPtr bitmap = Native.CreateCompatibleBitmap(screenDc, width, height);
        IntPtr previous = Native.SelectObject(memoryDc, bitmap);

        try
        {
            if (!Native.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Native.SRCCOPY))
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Native.SelectObject(memoryDc, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
