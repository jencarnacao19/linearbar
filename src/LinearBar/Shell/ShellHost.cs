using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Dock;
using LinearBar.Infrastructure;
using LinearBar.Metrics;
using LinearBar.Visuals;
using LinearBar.Widgets;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Enums;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Interop;

namespace LinearBar.Shell;

/// <summary>
/// Composition root: creates the shell services, the view models and the bar window, and
/// tears them down again in the right order.
/// </summary>
internal sealed class ShellHost : IDisposable
{
    private readonly SettingsStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly Log.ShellLogBridge _shellLog = new();
    private readonly bool _keepTaskbar;
    private BackdropWindow? _backdrop;
    private bool _disposed;

    public ShellHost(SettingsStore store, StartupOptions options, Action exit)
    {
        _store = store;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _keepTaskbar = options.KeepTaskbar;
        BarSettings settings = store.Current;

        // ManagedShell buffers its log until someone listens, so always attach.
        ShellLogger.Severity = options.Verbose ? LogSeverity.Debug : LogSeverity.Info;
        ShellLogger.Attach(_shellLog, flushOrphanedEvents: true);

        // Explorer normally is the shell; if it is not running we have to behave like one.
        EnvironmentHelper.IsAppRunningAsShell = NativeMethods.GetShellWindow() == IntPtr.Zero;

        ShellConfig config = ShellManager.DefaultShellConfig;
        config.TaskIconSize = IconSize.Jumbo;
        config.EnableTrayService = settings.ShowTray;
        config.AutoStartTrayService = settings.ShowTray;
        Shell = new ShellManager(config);

        Taskbar = new TaskbarGuard(Shell.ExplorerHelper);
        Taskbar.RecoverFromUncleanExit();

        ImportTaskbarPinsOnFirstRun();

        Dock = new DockViewModel(Shell, store, new AppIconLoader());
        Metrics = new MetricsViewModel(new WindowsMetricsSource(), settings.Metrics);
        Clock = new ClockViewModel(settings.Clock);
        ViewModel = new BarViewModel(Shell, store, Dock, Metrics, Clock, exit);

        ApplyAppearance(settings);
        Window = new BarWindow(Shell, AppBarScreen.FromPrimaryScreen(), ViewModel) { Backdrop = _backdrop };
        Window.Show();

        // Hide the Windows taskbar only once our own bar is up.
        if (settings.HideWindowsTaskbar && !_keepTaskbar)
        {
            Taskbar.Hide();
        }

        store.Changed += OnSettingsFileChanged;
        store.StartWatching();

        Log.Info($"Bar started on {Window.Screen.DeviceName} ({Window.Screen.Bounds.Width}x{Window.Screen.Bounds.Height}), "
            + $"{Dock.Items.Count} dock item(s), tray {(Shell.NotificationArea is null ? "off" : "on")}.");
    }

    public ShellManager Shell { get; }

    public TaskbarGuard Taskbar { get; }

    public DockViewModel Dock { get; }

    public MetricsViewModel Metrics { get; }

    public ClockViewModel Clock { get; }

    public BarViewModel ViewModel { get; }

    public BarWindow Window { get; }

    /// <summary>The blur behind the glass, or null when switched off or unsupported.</summary>
    public BackdropWindow? Backdrop => _backdrop;

    /// <summary>
    /// Last-resort clean-up for a process that is about to die from an unhandled exception:
    /// only what the user would otherwise be stuck with.
    /// </summary>
    public void EmergencyRestore()
    {
        try
        {
            Taskbar.Restore();
        }
        catch (Exception ex)
        {
            Log.Error("Could not restore the Windows taskbar", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnSettingsFileChanged;

        // Give the Windows taskbar back first: whatever else fails, the user has a taskbar.
        Step("restore the Windows taskbar", Taskbar.Restore);
        Step("close the bar", () =>
        {
            Shell.AppBarManager.SignalGracefulShutdown();
            Window.AllowClose = true;
            Window.Close();
        });
        Step("remove the backdrop", () => _backdrop?.Dispose());
        Step("stop the metrics", Metrics.Dispose);
        Step("stop the clock", Clock.Dispose);
        Step("release the dock", Dock.Dispose);
        Step("shut down the shell services", Shell.Dispose);

        ShellLogger.Detach(_shellLog);
    }

    private static void Step(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Shutdown: could not " + what, ex);
        }
    }

    /// <summary>Creates or removes the backdrop blur and configures the glass to match.</summary>
    private void ApplyAppearance(BarSettings settings)
    {
        if (settings.Appearance.Blur && _backdrop is null)
        {
            try
            {
                _backdrop = new BackdropWindow();
                if (!_backdrop.IsBlurEnabled)
                {
                    _backdrop.Dispose();
                    _backdrop = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not create the backdrop blur: " + ex.Message);
                _backdrop = null;
            }
        }
        else if (!settings.Appearance.Blur && _backdrop is not null)
        {
            _backdrop.Dispose();
            _backdrop = null;
        }

        Glass.Configure(_backdrop is not null, settings.BarOpacity, settings.Appearance.CornerSmoothing);
    }

    private void ImportTaskbarPinsOnFirstRun()
    {
        BarSettings settings = _store.Current;
        if (settings.ImportedTaskbarPins)
        {
            return;
        }

        List<PinnedApp> imported = settings.Pinned.Count == 0 ? TaskbarPinsImporter.Import() : [];
        _store.Update(current =>
        {
            current.Pinned.AddRange(imported);
            current.ImportedTaskbarPins = true;
        });
    }

    private void OnSettingsFileChanged(object? sender, EventArgs e)
    {
        // Raised on a thread-pool thread by the file watcher.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            Log.Info("Settings file changed; applying.");
            try
            {
                ApplyAppearance(_store.Current);
                Window.Backdrop = _backdrop;
                ViewModel.ApplySettings();
                Window.ApplyGeometry();

                if (_store.Current.HideWindowsTaskbar && !_keepTaskbar)
                {
                    Taskbar.Hide();
                }
                else
                {
                    Taskbar.Restore();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not apply the changed settings", ex);
            }
        });
    }
}
