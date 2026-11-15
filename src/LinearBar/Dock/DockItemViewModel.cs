using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;
using LinearBar.Infrastructure;
using LinearBar.Shell;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
using static ManagedShell.Interop.NativeMethods;

namespace LinearBar.Dock;

/// <summary>An entry of a dock icon's context menu; a null header is a separator.</summary>
internal sealed record DockMenuEntry(string? Header, Action? Action)
{
    public static DockMenuEntry Separator { get; } = new(null, null);

    public bool IsSeparator => Header is null;
}

/// <summary>
/// One icon in the dock: an application, which may be pinned, running (with one or more
/// windows), or both.
/// </summary>
internal sealed class DockItemViewModel : ObservableObject, IDisposable
{
    /// <summary>ITaskbarList3 progress values are reported on a 0–65534 scale.</summary>
    private const double ProgressScale = 65534;

    private readonly DockViewModel _owner;
    private readonly List<ApplicationWindow> _windows = [];

    private PinnedApp? _pin;
    private string? _identitySource;
    private ImageSource? _loadedIcon;
    private string? _loadedName;
    private IntPtr _activeAtPress;

    private ImageSource? _icon;
    private string _displayName = string.Empty;
    private bool _isPinned;
    private bool _isRunning;
    private bool _isActive;
    private bool _needsAttention;
    private int _indicatorCount;
    private bool _hasProgress;
    private double _progress;
    private bool _isProgressIndeterminate;
    private bool _isProgressError;

    public DockItemViewModel(string key, DockViewModel owner)
    {
        Key = key;
        _owner = owner;
        ClickCommand = new RelayCommand(Click);
    }

    /// <summary>App key (see <see cref="AppKey"/>).</summary>
    public string Key { get; }

    public ICommand ClickCommand { get; }

    public IReadOnlyList<ApplicationWindow> Windows => _windows;

    public ImageSource? Icon
    {
        get => _icon;
        private set => Set(ref _icon, value);
    }

    /// <summary>Application name shown in the label above the icon.</summary>
    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        private set => Set(ref _isPinned, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => Set(ref _isRunning, value);
    }

    /// <summary>True when one of the app's windows is the foreground window.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => Set(ref _isActive, value);
    }

    /// <summary>True when a window of the app is flashing for attention.</summary>
    public bool NeedsAttention
    {
        get => _needsAttention;
        private set => Set(ref _needsAttention, value);
    }

    /// <summary>Number of running-indicator dots: one per window, at most three.</summary>
    public int IndicatorCount
    {
        get => _indicatorCount;
        private set => Set(ref _indicatorCount, value);
    }

    /// <summary>True when a window reports taskbar progress (download, copy, render…).</summary>
    public bool HasProgress
    {
        get => _hasProgress;
        private set => Set(ref _hasProgress, value);
    }

    /// <summary>Progress from 0 to 1.</summary>
    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => Set(ref _isProgressIndeterminate, value);
    }

    public bool IsProgressError
    {
        get => _isProgressError;
        private set => Set(ref _isProgressError, value);
    }

    /// <summary>Called by <see cref="DockViewModel"/> whenever the pin or the window list changes.</summary>
    public void Update(PinnedApp? pin, IReadOnlyList<ApplicationWindow> windows)
    {
        foreach (ApplicationWindow window in _windows)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }

        _windows.Clear();
        _windows.AddRange(windows);

        foreach (ApplicationWindow window in _windows)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
        }

        _pin = pin;
        IsPinned = pin is not null;

        RefreshIdentity();
        RefreshState();
    }

    /// <summary>
    /// Remembers which of the app's windows was in front when the mouse button went down.
    /// It has to be captured now: by the time the click arrives, the bar itself has focus
    /// and no app window is active any more.
    /// </summary>
    public void NotePressed()
    {
        _activeAtPress = _owner.FindActiveWindow(_windows);
    }

    /// <summary>Left click: launch, activate, cycle or minimise (see <see cref="DockClick"/>).</summary>
    public void Click()
    {
        var ids = new List<long>(_windows.Count);
        foreach (ApplicationWindow window in _windows)
        {
            ids.Add(window.Handle.ToInt64());
        }

        DockClickResult result = DockClick.Decide(ids, _activeAtPress.ToInt64(), id => _owner.ActivationStamp(new IntPtr(id)));
        _activeAtPress = IntPtr.Zero;

        switch (result.Action)
        {
            case DockClickAction.Launch:
                Launch();
                break;
            case DockClickAction.Activate:
                FindWindow(result.WindowId)?.BringToFront();
                break;
            case DockClickAction.Minimize:
                FindWindow(result.WindowId)?.Minimize();
                break;
        }
    }

    /// <summary>Starts the app (a new instance or window when it is already running).</summary>
    public void Launch()
    {
        if (LaunchTarget() is not { } target)
        {
            return;
        }

        Log.Info($"Launching {DisplayName}: {target.File} {target.Arguments}".TrimEnd());
        ShellHelper.StartProcess(target.File, target.Arguments);
    }

    public void CloseAll()
    {
        foreach (ApplicationWindow window in _windows.ToArray())
        {
            window.Close();
        }
    }

    public void TogglePin()
    {
        if (IsPinned)
        {
            _owner.Unpin(Key);
        }
        else if (CreatePin() is { } pin)
        {
            _owner.Pin(pin);
        }
    }

    public IReadOnlyList<DockMenuEntry> BuildMenu()
    {
        var entries = new List<DockMenuEntry>();

        foreach (ApplicationWindow window in _windows)
        {
            ApplicationWindow target = window;
            entries.Add(new DockMenuEntry(Shorten(target.Title, DisplayName), () => target.BringToFront()));
        }

        if (entries.Count > 0)
        {
            entries.Add(DockMenuEntry.Separator);
        }

        if (LaunchTarget() is not null)
        {
            entries.Add(new DockMenuEntry(IsRunning ? Strings.NewWindow : Strings.Open, Launch));
        }

        if (IsPinned || CreatePin() is not null)
        {
            entries.Add(new DockMenuEntry(IsPinned ? Strings.Unpin : Strings.Pin, TogglePin));
        }

        if (_windows.Count > 0)
        {
            entries.Add(DockMenuEntry.Separator);
            entries.Add(new DockMenuEntry(_windows.Count == 1 ? Strings.CloseWindow : Strings.CloseAllWindows, CloseAll));
        }

        return entries;
    }

    public void Dispose()
    {
        foreach (ApplicationWindow window in _windows)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }

        _windows.Clear();
    }

    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ManagedShell raises some of these (icons) from its icon thread.
        if (_owner.Dispatcher.CheckAccess())
        {
            RefreshState();
        }
        else
        {
            _owner.Dispatcher.BeginInvoke(RefreshState);
        }
    }

    private void RefreshState()
    {
        bool active = false;
        bool attention = false;
        ApplicationWindow? progressWindow = null;

        foreach (ApplicationWindow window in _windows)
        {
            active |= window.State == ApplicationWindow.WindowState.Active;
            attention |= window.State == ApplicationWindow.WindowState.Flashing;
            if (progressWindow is null && window.ProgressState != TBPFLAG.TBPF_NOPROGRESS)
            {
                progressWindow = window;
            }
        }

        IsRunning = _windows.Count > 0;
        IndicatorCount = Math.Min(3, _windows.Count);
        IsActive = active;
        NeedsAttention = attention;

        HasProgress = progressWindow is not null;
        Progress = progressWindow is null ? 0 : Math.Clamp(progressWindow.ProgressValue / ProgressScale, 0, 1);
        IsProgressIndeterminate = progressWindow?.ProgressState == TBPFLAG.TBPF_INDETERMINATE;
        IsProgressError = progressWindow?.ProgressState is TBPFLAG.TBPF_ERROR or TBPFLAG.TBPF_PAUSED;

        // Until (or unless) the large icon loads, show the window's own icon.
        Icon = _loadedIcon ?? (_windows.Count > 0 ? _windows[0].Icon : null);
        DisplayName = ResolveName();
    }

    /// <summary>Starts loading the large icon and name when the app's identity changed.</summary>
    private void RefreshIdentity()
    {
        string? source;
        Task<AppIcon?>? load = null;

        if (AppKey.IsAumid(Key))
        {
            string? aumid = !string.IsNullOrWhiteSpace(_pin?.Aumid) ? _pin!.Aumid : FirstWindow()?.AppUserModelID;
            source = string.IsNullOrWhiteSpace(aumid) ? null : "aumid:" + aumid;
            if (source is not null && source != _identitySource)
            {
                load = _owner.Icons.LoadStoreAppAsync(aumid!);
            }
        }
        else
        {
            string? path = !string.IsNullOrWhiteSpace(_pin?.Path) ? _pin!.Path : FirstWindow()?.WinFileName;
            source = string.IsNullOrWhiteSpace(path) ? null : "file:" + path;
            if (source is not null && source != _identitySource)
            {
                load = _owner.Icons.LoadFileAsync(path!);
            }
        }

        if (source == _identitySource)
        {
            return;
        }

        _identitySource = source;
        _loadedIcon = null;
        _loadedName = null;

        load?.ContinueWith(
            task =>
            {
                // Ignore results that arrive after the identity changed again.
                if (_identitySource != source || task.Result is not { } result)
                {
                    return;
                }

                _loadedIcon = result.Image;
                _loadedName = result.DisplayName;
                RefreshState();
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            _owner.UiScheduler);
    }

    private string ResolveName()
    {
        if (!string.IsNullOrWhiteSpace(_pin?.Name))
        {
            return _pin!.Name!;
        }

        if (!string.IsNullOrWhiteSpace(_loadedName))
        {
            return _loadedName!;
        }

        ApplicationWindow? window = FirstWindow();
        if (window is not null && !AppKey.IsAumid(Key) && !string.IsNullOrWhiteSpace(window.WinFileName))
        {
            return Path.GetFileNameWithoutExtension(window.WinFileName);
        }

        return window?.Title ?? string.Empty;
    }

    private (string File, string Arguments)? LaunchTarget()
    {
        if (_pin is { } pin)
        {
            if (!string.IsNullOrWhiteSpace(pin.Aumid))
            {
                return ("appx:" + pin.Aumid, string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(pin.Path))
            {
                return (pin.Path, pin.Arguments ?? string.Empty);
            }
        }

        if (FirstWindow() is not { } window)
        {
            return null;
        }

        if (window.IsUWP)
        {
            // ManagedShell's StartProcess understands "appx:<AUMID>" for Store apps.
            return string.IsNullOrWhiteSpace(window.AppUserModelID) ? null : ("appx:" + window.AppUserModelID, string.Empty);
        }

        return string.IsNullOrWhiteSpace(window.WinFileName) ? null : (window.WinFileName, string.Empty);
    }

    private PinnedApp? CreatePin()
    {
        if (FirstWindow() is not { } window)
        {
            return null;
        }

        if (window.IsUWP)
        {
            return string.IsNullOrWhiteSpace(window.AppUserModelID)
                ? null
                : new PinnedApp { Name = DisplayName, Aumid = window.AppUserModelID };
        }

        return string.IsNullOrWhiteSpace(window.WinFileName)
            ? null
            : new PinnedApp { Name = DisplayName, Path = window.WinFileName };
    }

    private ApplicationWindow? FirstWindow() => _windows.Count > 0 ? _windows[0] : null;

    private ApplicationWindow? FindWindow(long id)
    {
        foreach (ApplicationWindow window in _windows)
        {
            if (window.Handle.ToInt64() == id)
            {
                return window;
            }
        }

        return null;
    }

    private static string Shorten(string? title, string fallback)
    {
        const int max = 64;
        if (string.IsNullOrWhiteSpace(title))
        {
            return fallback;
        }

        // An underscore would be read as a keyboard accelerator by the menu.
        title = title.Replace("_", "__");
        return title.Length <= max ? title : title[..(max - 1)] + "…";
    }
}
