namespace LinearBar.Core.Metrics;

/// <summary>
/// Fixed-capacity ring buffer of samples, oldest first. Backs the sparkline graphs.
/// Not thread-safe: written and read on the UI thread.
/// </summary>
public sealed class SampleHistory
{
    private double[] _values;
    private int _start;

    public SampleHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _values = new double[capacity];
    }

    /// <summary>Maximum number of samples kept.</summary>
    public int Capacity => _values.Length;

    /// <summary>Number of samples currently stored.</summary>
    public int Count { get; private set; }

    /// <summary>Incremented on every change; lets views detect that a redraw is needed.</summary>
    public int Version { get; private set; }

    /// <summary>Most recent sample, or 0 when empty.</summary>
    public double Latest => Count == 0 ? 0 : this[Count - 1];

    /// <summary>Sample at <paramref name="index"/>, where 0 is the oldest.</summary>
    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _values[(_start + index) % _values.Length];
        }
    }

    /// <summary>Appends a sample, dropping the oldest one when full. Non-finite values are stored as 0.</summary>
    public void Add(double value)
    {
        if (!double.IsFinite(value))
        {
            value = 0;
        }

        if (Count < _values.Length)
        {
            _values[(_start + Count) % _values.Length] = value;
            Count++;
        }
        else
        {
            _values[_start] = value;
            _start = (_start + 1) % _values.Length;
        }

        Version++;
    }

    /// <summary>Largest stored sample, or 0 when empty.</summary>
    public double Max()
    {
        double max = 0;
        for (int i = 0; i < Count; i++)
        {
            max = Math.Max(max, this[i]);
        }

        return max;
    }

    /// <summary>Removes all samples.</summary>
    public void Clear()
    {
        _start = 0;
        Count = 0;
        Version++;
    }

    /// <summary>Changes the capacity, keeping the most recent samples that still fit.</summary>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (capacity == _values.Length)
        {
            return;
        }

        int keep = Math.Min(Count, capacity);
        var values = new double[capacity];
        for (int i = 0; i < keep; i++)
        {
            values[i] = this[Count - keep + i];
        }

        _values = values;
        _start = 0;
        Count = keep;
        Version++;
    }
}
