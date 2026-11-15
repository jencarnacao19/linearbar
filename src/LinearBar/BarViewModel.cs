using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;
using LinearBar.Dock;
using LinearBar.Infrastructure;
using LinearBar.Metrics;
using LinearBar.Widgets;
using ManagedShell;
using ManagedShell.Common.Helpers;

namespace LinearBar;

/// <summary>Everything the bar window binds to.</summary>
internal sealed class BarViewModel : ObservableObject
{
    private static readonly Color BarColor = Color.FromRgb(0x1C, 0x1C, 0x21);

    private readonly SettingsStore _store;
    private BarGeometry _geometry;
    private Brush _barBackground = Brushes.Transparent;
    private bool _showTray;
    private bool _isTrayExpanded;

    public BarViewModel(ShellManager shell, SettingsStore store, DockViewModel dock, MetricsViewModel metrics, ClockViewModel clock, Action exit)
    {
        _store = store;
        Dock = dock;
        Metrics = metrics;
        Clock = clock;

        // Null when the tray service is switched off in the settings.
        PinnedTrayIcons = shell.NotificationArea?.PinnedIcons;
        UnpinnedTrayIcons = shell.NotificationArea?.UnpinnedIcons;

        StartCommand = new RelayCommand(ShellHelper.ShowStartMenu);
        StartMenuCommand = new RelayCommand(ShellHelper.ShowStartContextMenu);
        TaskManagerCommand = new RelayCommand(ShellHelper.StartTaskManager);
        NotificationCenterCommand = new RelayCommand(ShowNotifications);
        OpenSettingsCommand = new RelayCommand(() => OpenFile(store.FilePath));
        OpenLogCommand = new RelayCommand(() => OpenFile(Log.FilePath));
        ExitCommand = new RelayCommand(exit);

        // The parts were just created from the same settings; only our own values are missing.
        ApplyOwnSettings();
    }

    public DockViewModel Dock { get; }

    public MetricsViewModel Metrics { get; }

    public ClockViewModel Clock { get; }

    public ICollectionView? PinnedTrayIcons { get; }

    public ICollectionView? UnpinnedTrayIcons { get; }

    public ICommand StartCommand { get; }

    public ICommand StartMenuCommand { get; }

    public ICommand TaskManagerCommand { get; }

    public ICommand NotificationCenterCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenLogCommand { get; }

    public ICommand ExitCommand { get; }

    /// <summary>Sizes derived from the settings.</summary>
    public BarGeometry Geometry => _geometry;

    /// <summary>Height of the visible strip.</summary>
    public double BarHeight => _geometry.BarHeight;

    public Brush BarBackground
    {
        get => _barBackground;
        private set => Set(ref _barBackground, value);
    }

    public bool ShowTray
    {
        get => _showTray;
        private set => Set(ref _showTray, value);
    }

    /// <summary>True while the icons that are not pinned to the tray are shown too.</summary>
    public bool IsTrayExpanded
    {
        get => _isTrayExpanded;
        set => Set(ref _isTrayExpanded, value);
    }

    /// <summary>Re-reads the settings and pushes them to every part of the bar.</summary>
    public void ApplySettings()
    {
        BarSettings settings = _store.Current;

        ApplyOwnSettings();
        Dock.ApplySettings();
        Metrics.ApplySettings(settings.Metrics);
        Clock.ApplySettings(settings.Clock);
    }

    private void ApplyOwnSettings()
    {
        BarSettings settings = _store.Current;

        _geometry = BarGeometry.From(settings);
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(BarHeight));

        var background = new SolidColorBrush(BarColor) { Opacity = settings.BarOpacity };
        background.Freeze();
        BarBackground = background;

        ShowTray = settings.ShowTray && PinnedTrayIcons is not null;
    }

    private static void ShowNotifications()
    {
        if (EnvironmentHelper.IsWindows11OrBetter)
        {
            ShellHelper.ShowNotificationCenter();
        }
        else
        {
            ShellHelper.ShowActionCenter();
        }
    }

    private static void OpenFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Opens with whatever the user has associated with the file type.
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
