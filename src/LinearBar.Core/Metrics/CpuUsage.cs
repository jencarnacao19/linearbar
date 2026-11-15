namespace LinearBar.Core.Metrics;

/// <summary>
/// Cumulative processor times as returned by Win32 <c>GetSystemTimes</c>, summed over all
/// cores, in 100 ns ticks. Note that on Windows <see cref="Kernel"/> already includes
/// <see cref="Idle"/>.
/// </summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

public static class CpuUsage
{
    /// <summary>
    /// Total CPU load in percent (0–100) between two readings of the cumulative counters.
    /// Returns 0 when no time has passed or the counters went backwards.
    /// </summary>
    public static double Percent(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
        {
            return 0;
        }

        ulong idle = current.Idle - previous.Idle;
        ulong total = (current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total == 0)
        {
            return 0;
        }

        ulong busy = total > idle ? total - idle : 0;
        return Math.Clamp(100.0 * busy / total, 0, 100);
    }
}
