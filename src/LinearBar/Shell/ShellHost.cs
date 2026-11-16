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
        Plugins = new LinearBar.Plugins.PluginHost();
        Plugins.Apply(settings);
        ViewModel = new BarViewModel(Shell, store, Dock, Metrics, Clock, Plugins, exit);

        ApplyAppearance(settings);
        Window = CreateWindow();

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

    public LinearBar.Plugins.PluginHost Plugins { get; }

    public BarViewModel ViewModel { get; }

    public BarWindow Window { get; private set; }

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
        Step("close the customiser", () => _customizer?.Close());
        Step("close the bar", () =>
        {
            Shell.AppBarManager.SignalGracefulShutdown();
            Window.AllowClose = true;
            Window.Close();
        });
        Step("remove the backdrop", () => _backdrop?.Dispose());
        Step("stop the plugins", Plugins.Dispose);
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

        ThemeManager.Apply(settings, _backdrop is not null);
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

    /// <summary>Raised after the bar window was replaced (e.g. moved between top and bottom).</summary>
    public event Action<BarWindow>? WindowRecreated;

    /// <summary>
    /// Applies the current settings to everything that is running: theme, layout, sizes,
    /// plugins. Moving the bar between the top and bottom edge recreates its window.
    /// </summary>
    public void ApplySettings()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            BarSettings settings = _store.Current;
            ApplyAppearance(settings);
            ViewModel.ApplySettings();

            if (settings.Layout.IsTop != Window.IsTop)
            {
                RecreateWindow();
            }
            else
            {
                Window.Backdrop = _backdrop;
                Window.ApplyGeometry();
                Window.BuildLayout();
            }

            if (settings.HideWindowsTaskbar && !_keepTaskbar)
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
    }

    private BarWindow CreateWindow()
    {
        var window = new BarWindow(Shell, AppBarScreen.FromPrimaryScreen(), ViewModel) { Backdrop = _backdrop };
        window.CustomizeRequested += OpenCustomizer;
        LinearBar.Visuals.PopupPlacement.BarAtTop = window.IsTop;
        window.Show();
        return window;
    }

    private void RecreateWindow()
    {
        BarWindow old = Window;
        old.CustomizeRequested -= OpenCustomizer;
        old.Backdrop = null;
        old.AllowClose = true;
        old.Close();

        Window = CreateWindow();
        WindowRecreated?.Invoke(Window);
        Log.Info($"Bar moved to the {(Window.IsTop ? "top" : "bottom")} edge.");
    }

    private Settings.SettingsWindow? _customizer;

    /// <summary>Opens the customisation window (or brings it to the front).</summary>
    public void OpenCustomizer()
    {
        if (_customizer is { IsLoaded: true })
        {
            _customizer.Activate();
            return;
        }

        _customizer = new Settings.SettingsWindow(_store, ApplySettings, Plugins);
        _customizer.Closed += (_, _) => _customizer = null;
        _customizer.Show();
        _customizer.Activate();
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
            ApplySettings();
            _customizer?.Reload();
        });
    }
}
