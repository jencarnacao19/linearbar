using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LinearBar.Core.Metrics;
using LinearBar.Infrastructure;

namespace LinearBar.Metrics;

/// <summary>
/// Reads CPU, memory, network and battery figures with cheap Win32 calls (no performance
/// counters, no WMI), so sampling once a second costs next to nothing.
/// </summary>
internal sealed class WindowsMetricsSource : ISystemMetricsSource
{
    private readonly RateCounter _download = new();
    private readonly RateCounter _upload = new();
    private CpuTimes? _previousCpu;

    public MetricsSnapshot Sample()
    {
        double cpu = ReadCpu();
        (ulong used, ulong total) = ReadMemory();

        (long received, long sent) = ReadNetworkCounters();
        long now = Stopwatch.GetTimestamp();
        double down = _download.Update(received, now, Stopwatch.Frequency);
        double up = _upload.Update(sent, now, Stopwatch.Frequency);

        (int? battery, bool onAc) = ReadBattery();

        return new MetricsSnapshot
        {
            CpuPercent = cpu,
            MemoryUsedBytes = used,
            MemoryTotalBytes = total,
            NetworkDownBytesPerSecond = down,
            NetworkUpBytesPerSecond = up,
            BatteryPercent = battery,
            IsOnAcPower = onAc,
        };
    }

    private double ReadCpu()
    {
        if (!Native.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            return 0;
        }

        var current = new CpuTimes((ulong)idle, (ulong)kernel, (ulong)user);
        double percent = _previousCpu is { } previous ? CpuUsage.Percent(previous, current) : 0;
        _previousCpu = current;
        return percent;
    }

    private static (ulong Used, ulong Total) ReadMemory()
    {
        var status = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (!Native.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return (0, 0);
        }

        return (status.ullTotalPhys - Math.Min(status.ullAvailPhys, status.ullTotalPhys), status.ullTotalPhys);
    }

    /// <summary>Total bytes received and sent over all adapters that are up.</summary>
    private static (long Received, long Sent) ReadNetworkCounters()
    {
        long received = 0;
        long sent = 0;

        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceStatistics statistics = adapter.GetIPStatistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
            // Adapters can disappear between enumeration and query; the next sample recovers.
        }

        return (received, sent);
    }

    private static (int? Percent, bool OnAc) ReadBattery()
    {
        if (!Native.GetSystemPowerStatus(out Native.SYSTEM_POWER_STATUS status))
        {
            return (null, true);
        }

        bool onAc = status.ACLineStatus == 1;
        bool hasBattery = (status.BatteryFlag & Native.BatteryFlagNoBattery) == 0
            && status.BatteryFlag != 255
            && status.BatteryLifePercent != Native.BatteryPercentUnknown;

        return (hasBattery ? Math.Clamp((int)status.BatteryLifePercent, 0, 100) : null, onAc);
    }
}
