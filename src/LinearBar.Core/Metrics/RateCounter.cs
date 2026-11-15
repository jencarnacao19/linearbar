namespace LinearBar.Core.Metrics;

/// <summary>
/// Turns a monotonically increasing counter (bytes received, bytes written, …) into a
/// per-second rate.
/// </summary>
public sealed class RateCounter
{
    private bool _hasPrevious;
    private long _previousValue;
    private long _previousTimestamp;

    /// <summary>
    /// Feeds a new reading and returns the rate since the previous one.
    /// </summary>
    /// <param name="value">Cumulative counter value.</param>
    /// <param name="timestamp">Monotonic timestamp of the reading.</param>
    /// <param name="ticksPerSecond">Resolution of <paramref name="timestamp"/>.</param>
    /// <returns>
    /// Units per second; 0 for the first reading, when no time has passed, or when the
    /// counter went backwards (adapter reset, counter wrap, interface removed).
    /// </returns>
    public double Update(long value, long timestamp, long ticksPerSecond)
    {
        double rate = 0;

        if (_hasPrevious && ticksPerSecond > 0)
        {
            long elapsed = timestamp - _previousTimestamp;
            long delta = value - _previousValue;
            if (elapsed > 0 && delta > 0)
            {
                rate = delta * (double)ticksPerSecond / elapsed;
            }
        }

        _hasPrevious = true;
        _previousValue = value;
        _previousTimestamp = timestamp;
        return rate;
    }

    /// <summary>Forgets the previous reading; the next <see cref="Update"/> returns 0.</summary>
    public void Reset() => _hasPrevious = false;
}
