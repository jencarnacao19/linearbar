using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;
using LinearBar.Infrastructure;
using LinearBar.Shell;
using ManagedShell;
using ManagedShell.WindowsTasks;

namespace LinearBar.Dock;

/// <summary>
/// The dock: pinned apps plus running apps, one icon per application.
/// Bridges ManagedShell's task list to the pure planning logic in <c>LinearBar.Core.Dock</c>.
/// </summary>
internal sealed class DockViewModel : ObservableObject, IDisposable
{
    private readonly ShellManager _shell;
    private readonly SettingsStore _store;
    private readonly ICollectionView _taskWindows;

    private readonly Dictionary<IntPtr, long> _firstSeen = [];
    private readonly Dictionary<IntPtr, long> _lastActivated = [];
    private long _sequence;
    private long _activationCounter;
    private IntPtr _lastActiveTask;
    private bool _refreshQueued;
    private BarGeometry _geometry;
    private bool _showLabels;
    private bool _iconTiles;

    public DockViewModel(ShellManager shell, SettingsStore store, AppIconLoader icons)
    {
        _shell = shell;
        _store = store;
        Icons = icons;
        Dispatcher = Dispatcher.CurrentDispatcher;
        UiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

        ApplySettings();

        // The task list only contains windows that belong in a taskbar; ManagedShell keeps
        // it up to date from the shell hook and filters out tool windows, cloaked windows, etc.
        _taskWindows = shell.Tasks.GroupedWindows;
        _taskWindows.CollectionChanged += OnTaskWindowsChanged;
        shell.TasksService.WindowActivated += OnWindowActivated;
        shell.TasksService.DesktopActivated += OnDesktopActivated;

        Refresh();
    }

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public AppIconLoader Icons { get; }

    public Dispatcher Dispatcher { get; }

    public TaskScheduler UiScheduler { get; }

    // ---- Layout values the view binds to ------------------------------------------------

    public double IconSize => _geometry.IconSize;

    public double SlotWidth => _geometry.SlotWidth;

    public double CellHeight => _geometry.CellHeight;

    public double CellBottomMargin => _geometry.CellBottomMargin;

    public double MaxScale => _geometry.MaxScale;

    public double LensRadius => _geometry.LensRadius;

    public bool ShowLabels => _showLabels;

    public bool IconTiles => _iconTiles;

    public double PlatterHeight => _geometry.IslandHeight;

    public double PlatterBottom => BarGeometry.IslandMargin;

    public double PlatterRadius => _geometry.IslandRadius;

    /// <summary>Re-reads the settings (sizes, pins) and rebuilds the dock.</summary>
    public void ApplySettings()
    {
        BarSettings settings = _store.Current;
        _geometry = BarGeometry.From(settings);
        _showLabels = settings.Dock.ShowLabels;
        _iconTiles = settings.Dock.IconTiles;

        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(SlotWidth));
        OnPropertyChanged(nameof(CellHeight));
        OnPropertyChanged(nameof(CellBottomMargin));
        OnPropertyChanged(nameof(MaxScale));
        OnPropertyChanged(nameof(LensRadius));
        OnPropertyChanged(nameof(ShowLabels));
        OnPropertyChanged(nameof(IconTiles));
        OnPropertyChanged(nameof(PlatterHeight));
        OnPropertyChanged(nameof(PlatterBottom));
        OnPropertyChanged(nameof(PlatterRadius));

        if (_taskWindows is not null)
        {
            Refresh();
        }
    }

    /// <summary>Rebuilds the icon list from the pinned apps and the current task windows.</summary>
    public void Refresh()
    {
        _refreshQueued = false;

        var windowsById = new Dictionary<long, ApplicationWindow>();
        var refs = new List<WindowRef>();

        foreach (object item in _taskWindows)
        {
            if (item is not ApplicationWindow window)
            {
                continue;
            }

            long id = window.Handle.ToInt64();
            if (!windowsById.TryAdd(id, window))
            {
                continue;
            }

            if (!_firstSeen.TryGetValue(window.Handle, out long sequence))
            {
                sequence = ++_sequence;
                _firstSeen[window.Handle] = sequence;
            }

            refs.Add(new WindowRef(id, KeyOf(window), sequence));
        }

        Forget(_firstSeen, windowsById);
        Forget(_lastActivated, windowsById);

        List<PinnedApp> pinned = _store.Current.Pinned;
        var pinnedKeys = new List<string?>(pinned.Count);
        foreach (PinnedApp pin in pinned)
        {
            pinnedKeys.Add(AppKey.ForPinned(pin));
        }

        List<DockPlanEntry> plan = DockPlanner.Plan(pinnedKeys, refs);

        var keys = new List<string>(plan.Count);
        foreach (DockPlanEntry entry in plan)
        {
            keys.Add(entry.Key);
        }

        CollectionSync.Apply(Items, keys, item => item.Key, key => new DockItemViewModel(key, this), item => item.Dispose());

        for (int i = 0; i < plan.Count; i++)
        {
            DockPlanEntry entry = plan[i];
            var windows = new List<ApplicationWindow>(entry.WindowIds.Count);
            foreach (long id in entry.WindowIds)
            {
                windows.Add(windowsById[id]);
            }

            Items[i].Update(entry.IsPinned ? pinned[entry.PinnedIndex] : null, windows);
        }
    }

    /// <summary>
    /// The window among <paramref name="windows"/> that is (or was, a moment ago) in the
    /// foreground, or zero.
    /// </summary>
    /// <remarks>
    /// Clicking the bar gives it focus, and the shell reports the app window as inactive
    /// shortly after. Normally the mouse-down handler still sees the old state; if the
    /// notification already arrived, fall back to the task window that was active last, as
    /// long as nothing but LinearBar took the foreground since.
    /// </remarks>
    public IntPtr FindActiveWindow(IReadOnlyList<ApplicationWindow> windows)
    {
        foreach (ApplicationWindow window in windows)
        {
            if (window.State == ApplicationWindow.WindowState.Active)
            {
                return window.Handle;
            }
        }

        if (_lastActiveTask == IntPtr.Zero || Native.IsIconic(_lastActiveTask) || !ForegroundIsOurs())
        {
            return IntPtr.Zero;
        }

        foreach (ApplicationWindow window in windows)
        {
            if (window.Handle == _lastActiveTask)
            {
                return window.Handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>When <paramref name="window"/> was last activated; larger is more recent, 0 is never.</summary>
    public long ActivationStamp(IntPtr window) => _lastActivated.GetValueOrDefault(window);

    public void Pin(PinnedApp pin)
    {
        _store.Update(settings => settings.Pinned.Add(pin));
        Refresh();
    }

    public void Unpin(string key)
    {
        _store.Update(settings => settings.Pinned.RemoveAll(pin => AppKey.ForPinned(pin) == key));
        Refresh();
    }

    public void Dispose()
    {
        _taskWindows.CollectionChanged -= OnTaskWindowsChanged;
        _shell.TasksService.WindowActivated -= OnWindowActivated;
        _shell.TasksService.DesktopActivated -= OnDesktopActivated;

        foreach (DockItemViewModel item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
    }

    private static string KeyOf(ApplicationWindow window)
    {
        bool isStoreApp = window.IsUWP;
        return AppKey.ForWindow(window.WinFileName, isStoreApp ? window.AppUserModelID : null, isStoreApp)
            // No executable path (protected process): still show the window, as its own app.
            ?? "window:" + window.Handle.ToInt64();
    }

    private static void Forget(Dictionary<IntPtr, long> map, Dictionary<long, ApplicationWindow> alive)
    {
        if (map.Count == 0)
        {
            return;
        }

        List<IntPtr>? gone = null;
        foreach (IntPtr handle in map.Keys)
        {
            if (!alive.ContainsKey(handle.ToInt64()))
            {
                (gone ??= []).Add(handle);
            }
        }

        if (gone is not null)
        {
            foreach (IntPtr handle in gone)
            {
                map.Remove(handle);
            }
        }
    }

    private static bool ForegroundIsOurs()
    {
        IntPtr foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        Native.GetWindowThreadProcessId(foreground, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    private void OnTaskWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Opening an app can add and remove several windows in a burst; rebuild once.
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
    }

    private void OnWindowActivated(object? sender, WindowEventArgs e)
    {
        // ManagedShell also reports our own bar window here; only real task windows count.
        if (e.Window is not { ShowInTaskbar: true } window)
        {
            return;
        }

        _lastActiveTask = window.Handle;
        _lastActivated[window.Handle] = ++_activationCounter;
    }

    private void OnDesktopActivated(object? sender, EventArgs e)
    {
        _lastActiveTask = IntPtr.Zero;
    }
}
