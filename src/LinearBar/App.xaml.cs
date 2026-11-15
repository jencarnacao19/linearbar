using System.Windows;
using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Infrastructure;
using LinearBar.Shell;

namespace LinearBar;

/// <summary>Application entry point: start-up, shutdown and crash handling.</summary>
public partial class App : Application
{
    private const string SingleInstanceName = @"Local\LinearBar.SingleInstance";

    private Mutex? _singleInstance;
    private SettingsStore? _store;
    private ShellHost? _host;
    private StartupOptions _options = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _options = StartupOptions.Parse(e.Args);
        Log.Initialize(_options.LogPath, _options.Verbose || _options.SmokeTest);
        Log.Info($"LinearBar {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion.VersionString}.");

        foreach (string unknown in _options.Unknown)
        {
            Log.Warn("Ignoring unknown argument: " + unknown);
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (_options.RestoreTaskbar)
        {
            TaskbarGuard.ForceRestore();
            Quit(0);
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Warn("LinearBar is already running; exiting.");
            _singleInstance.Dispose();
            _singleInstance = null;
            Quit(0);
            return;
        }

        try
        {
            _store = new SettingsStore(_options.SettingsPath ?? SettingsStore.DefaultPath);
            _store.Load();
            if (_store.LastError is { } error)
            {
                Log.Warn($"Settings file '{_store.FilePath}' could not be read ({error}); using defaults.");
            }

            _host = new ShellHost(_store, _options, () => Quit(0));
        }
        catch (Exception ex)
        {
            Log.Error("Start-up failed", ex);
            Quit(1);
            return;
        }

        if (_options.SmokeTest)
        {
            new SmokeTest(_host, _options, Quit).Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        _store?.Dispose();

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }

        Log.Info($"LinearBar exited with code {e.ApplicationExitCode}.");
        Log.Close();
        base.OnExit(e);
    }

    /// <summary>Ends the application; the clean-up happens in <see cref="OnExit"/>.</summary>
    private void Quit(int code)
    {
        Environment.ExitCode = code;
        Shutdown(code);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A taskbar should survive a bug in one of its widgets: log it and carry on.
        Log.Error("Unhandled exception on the UI thread", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Fatal unhandled exception", e.ExceptionObject as Exception);

        // The process is going down: at least give the user their Windows taskbar back.
        _host?.EmergencyRestore();
        Log.Close();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
