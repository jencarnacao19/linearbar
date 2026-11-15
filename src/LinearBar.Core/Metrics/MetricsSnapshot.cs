namespace LinearBar.Core.Metrics;

/// <summary>One reading of the system metrics shown in the bar.</summary>
public sealed record MetricsSnapshot
{
    /// <summary>Total CPU load, 0–100.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Physical memory in use, in bytes.</summary>
    public ulong MemoryUsedBytes { get; init; }

    /// <summary>Installed physical memory, in bytes.</summary>
    public ulong MemoryTotalBytes { get; init; }

    /// <summary>Download rate summed over active network adapters, bytes per second.</summary>
    public double NetworkDownBytesPerSecond { get; init; }

    /// <summary>Upload rate summed over active network adapters, bytes per second.</summary>
    public double NetworkUpBytesPerSecond { get; init; }

    /// <summary>Battery charge 0–100, or null on machines without a battery.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>True when running on mains power.</summary>
    public bool IsOnAcPower { get; init; }

    /// <summary>Memory in use as a percentage of installed memory, 0–100.</summary>
    public double MemoryPercent => MemoryTotalBytes == 0 ? 0 : Math.Clamp(100.0 * MemoryUsedBytes / MemoryTotalBytes, 0, 100);
}

/// <summary>Source of raw system readings. Implemented with Win32 calls in the app project.</summary>
public interface ISystemMetricsSource
{
    /// <summary>Takes a reading. Called from a background thread, never concurrently.</summary>
    MetricsSnapshot Sample();
}
