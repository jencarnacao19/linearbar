using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using LinearBar.Core.Config;
using LinearBar.Core.Metrics;
using LinearBar.Infrastructure;

namespace LinearBar.Metrics;

/// <summary>
/// Samples system metrics on a background thread and feeds the graphs shown in the bar.
/// </summary>
internal sealed class MetricsViewModel : ObservableObject, IDisposable
{
    /// <summary>The network graph never zooms in further than this (bytes per second).</summary>
    private const double MinimumNetworkScale = 100 * 1024;

    private readonly ISystemMetricsSource _source;
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _cancellation;
    private MetricItem? _cpu;
    private MetricItem? _memory;
    private MetricItem? _network;
    private bool _showBattery;
    private bool _batteryEnabled;
    private string _batteryText = string.Empty;
    private string _batteryDetail = string.Empty;
    private int _sampleCount;

    public MetricsViewModel(ISystemMetricsSource source, MetricsSettings settings)
    {
        _source = source;
        _dispatcher = Dispatcher.CurrentDispatcher;
        ApplySettings(settings);
    }

    /// <summary>The graphs that are switched on, in display order.</summary>
    public ObservableCollection<MetricItem> Items { get; } = [];

    /// <summary>True on machines with a battery, when the battery read-out is enabled.</summary>
    public bool ShowBattery
    {
        get => _showBattery;
        private set => Set(ref _showBattery, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        private set => Set(ref _batteryText, value);
    }

    public string BatteryDetail
    {
        get => _batteryDetail;
        private set => Set(ref _batteryDetail, value);
    }

    /// <summary>Most recent reading, for diagnostics.</summary>
    public MetricsSnapshot? Latest { get; private set; }

    /// <summary>Number of readings taken since the last (re)start.</summary>
    public int SampleCount => _sampleCount;

    /// <summary>Rebuilds the graphs for new settings and restarts sampling.</summary>
    public void ApplySettings(MetricsSettings settings)
    {
        Stop();

        int length = settings.HistoryLength;
        _cpu = Reuse(_cpu, settings.Enabled && settings.ShowCpu, length, () => new MetricItem("cpu", Strings.Cpu, 58, 100, length, Palette.Cpu));
        _memory = Reuse(_memory, settings.Enabled && settings.ShowMemory, length, () => new MetricItem("memory", Strings.Memory, 58, 100, length, Palette.Memory));
        _network = Reuse(_network, settings.Enabled && settings.ShowNetwork, length, () => new MetricItem("network", Strings.Network, 92, MinimumNetworkScale, length, Palette.NetworkDown, Palette.NetworkUp));

        Items.Clear();
        foreach (MetricItem? item in new[] { _cpu, _memory, _network })
        {
            if (item is not null)
            {
                Items.Add(item);
            }
        }

        _batteryEnabled = settings.Enabled && settings.ShowBattery;
        if (!_batteryEnabled)
        {
            ShowBattery = false;
        }

        if (Items.Count > 0 || _batteryEnabled)
        {
            Start(TimeSpan.FromMilliseconds(settings.IntervalMs));
        }
    }

    public void Dispose() => Stop();

    private static MetricItem? Reuse(MetricItem? existing, bool enabled, int historyLength, Func<MetricItem> create)
    {
        if (!enabled)
        {
            return null;
        }

        if (existing is null)
        {
            return create();
        }

        // Keep the history across a settings reload so the graphs do not start empty again.
        existing.Resize(historyLength);
        return existing;
    }

    private void Start(TimeSpan interval)
    {
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _sampleCount = 0;
        _ = Task.Run(() => SampleLoopAsync(interval, cancellation.Token));
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task SampleLoopAsync(TimeSpan interval, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                MetricsSnapshot snapshot;
                try
                {
                    snapshot = _source.Sample();
                }
                catch (Exception ex)
                {
                    Log.Warn("Metrics sample failed: " + ex.Message);
                    continue;
                }

                // Sampling happens off the UI thread; the graphs are only touched on it.
                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        Apply(snapshot);
                    }
                });
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Metrics sampling stopped unexpectedly", ex);
        }
    }

    private void Apply(MetricsSnapshot snapshot)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        Latest = snapshot;
        _sampleCount++;

        if (_cpu is { } cpu)
        {
            cpu.Add(snapshot.CpuPercent);
            cpu.ValueText = snapshot.CpuPercent.ToString("0", culture) + " %";
            cpu.Detail = string.Format(culture, Strings.CpuDetail, snapshot.CpuPercent) + "\n" + Strings.MetricsHint;
        }

        if (_memory is { } memory)
        {
            memory.Add(snapshot.MemoryPercent);
            memory.ValueText = snapshot.MemoryPercent.ToString("0", culture) + " %";
            memory.Detail = string.Format(
                culture,
                Strings.MemoryDetail,
                ByteFormat.Size(snapshot.MemoryUsedBytes, culture),
                ByteFormat.Size(snapshot.MemoryTotalBytes, culture),
                snapshot.MemoryPercent) + "\n" + Strings.MetricsHint;
        }

        if (_network is { } network)
        {
            network.Add(snapshot.NetworkDownBytesPerSecond, snapshot.NetworkUpBytesPerSecond);

            // Both series share one axis, rounded up to a stable value so it does not twitch.
            double peak = Math.Max(network.History.Max(), network.SecondaryHistory?.Max() ?? 0);
            network.Maximum = NiceScale.Ceiling(peak, MinimumNetworkScale);

            string down = ByteFormat.Rate(snapshot.NetworkDownBytesPerSecond, culture);
            string up = ByteFormat.Rate(snapshot.NetworkUpBytesPerSecond, culture);
            network.ValueText = "↓ " + down;
            network.Detail = string.Format(culture, Strings.NetworkDetail, down, up) + "\n" + Strings.MetricsHint;
        }

        if (_batteryEnabled && snapshot.BatteryPercent is { } battery)
        {
            ShowBattery = true;
            BatteryText = battery.ToString(culture) + " %";
            BatteryDetail = string.Format(
                culture,
                Strings.BatteryDetail,
                battery,
                snapshot.IsOnAcPower ? Strings.BatteryCharging : Strings.BatteryDischarging);
        }
        else
        {
            ShowBattery = false;
        }
    }
}
