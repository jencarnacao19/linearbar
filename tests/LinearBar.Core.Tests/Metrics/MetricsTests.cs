using System.Globalization;
using LinearBar.Core.Metrics;

namespace LinearBar.Core.Tests.Metrics;

public class SampleHistoryTests
{
    [Fact]
    public void Keeps_the_most_recent_samples_oldest_first()
    {
        var history = new SampleHistory(3);
        Assert.Equal(0, history.Latest);

        history.Add(1);
        history.Add(2);
        Assert.Equal(2, history.Count);
        Assert.Equal(2, history.Latest);

        history.Add(3);
        history.Add(4);
        history.Add(5);

        Assert.Equal(3, history.Count);
        Assert.Equal([3.0, 4.0, 5.0], Enumerable.Range(0, history.Count).Select(i => history[i]));
        Assert.Equal(5, history.Latest);
        Assert.Equal(5, history.Max());
    }

    [Fact]
    public void Version_changes_with_every_mutation()
    {
        var history = new SampleHistory(2);
        int version = history.Version;

        history.Add(1);
        Assert.NotEqual(version, history.Version);

        version = history.Version;
        history.Clear();
        Assert.NotEqual(version, history.Version);
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Non_finite_samples_are_stored_as_zero_and_bad_indexes_throw()
    {
        var history = new SampleHistory(2);
        history.Add(double.NaN);
        history.Add(double.PositiveInfinity);

        Assert.Equal(0, history[0]);
        Assert.Equal(0, history[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => history[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleHistory(0));
    }

    [Fact]
    public void Resize_keeps_the_newest_samples()
    {
        var history = new SampleHistory(4);
        for (int i = 1; i <= 6; i++) history.Add(i); // holds 3,4,5,6

        history.Resize(2);
        Assert.Equal([5.0, 6.0], Enumerable.Range(0, history.Count).Select(i => history[i]));

        history.Resize(5);
        history.Add(7);
        Assert.Equal([5.0, 6.0, 7.0], Enumerable.Range(0, history.Count).Select(i => history[i]));
        Assert.Equal(5, history.Capacity);
    }
}

public class CpuUsageTests
{
    [Fact]
    public void Computes_load_from_counter_deltas()
    {
        // Kernel time includes idle time on Windows: 600 idle, 800 kernel (200 busy), 200 user.
        var previous = new CpuTimes(1000, 2000, 500);
        var current = new CpuTimes(1600, 2800, 700);

        Assert.Equal(40, CpuUsage.Percent(previous, current), 9);
    }

    [Fact]
    public void Idle_and_saturated_machines_report_the_extremes()
    {
        Assert.Equal(0, CpuUsage.Percent(new CpuTimes(0, 0, 0), new CpuTimes(1000, 1000, 0)));
        Assert.Equal(100, CpuUsage.Percent(new CpuTimes(0, 0, 0), new CpuTimes(0, 400, 600)));
    }

    [Fact]
    public void No_elapsed_time_or_counters_going_backwards_yield_zero()
    {
        var times = new CpuTimes(10, 20, 30);

        Assert.Equal(0, CpuUsage.Percent(times, times));
        Assert.Equal(0, CpuUsage.Percent(times, new CpuTimes(5, 20, 30)));
    }
}

public class RateCounterTests
{
    private const long Second = 10_000_000;

    [Fact]
    public void First_reading_has_no_rate()
    {
        Assert.Equal(0, new RateCounter().Update(5000, 0, Second));
    }

    [Fact]
    public void Rate_is_delta_per_second()
    {
        var counter = new RateCounter();
        counter.Update(1000, 0, Second);

        Assert.Equal(2048, counter.Update(1000 + 2048, Second, Second), 9);
        Assert.Equal(1000, counter.Update(1000 + 2048 + 500, Second + Second / 2, Second), 9);
    }

    [Fact]
    public void Counter_reset_and_stalled_clock_yield_zero_then_recover()
    {
        var counter = new RateCounter();
        counter.Update(10_000, 0, Second);

        Assert.Equal(0, counter.Update(100, Second, Second));        // counter went backwards
        Assert.Equal(0, counter.Update(900, Second, Second));        // no time passed
        Assert.Equal(100, counter.Update(1000, 2 * Second, Second), 9);

        counter.Reset();
        Assert.Equal(0, counter.Update(50_000, 3 * Second, Second));
    }
}

public class ByteFormatTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(12.34 * 1024, "12.3 KB")]
    [InlineData(512 * 1024, "512 KB")]
    [InlineData(1.25 * 1024 * 1024, "1.25 MB")]
    [InlineData(1023.9 * 1024, "1 MB")]
    [InlineData(7.8 * 1024 * 1024 * 1024, "7.8 GB")]
    [InlineData(-5, "0 B")]
    [InlineData(double.NaN, "0 B")]
    public void Size_uses_binary_units_and_about_three_significant_digits(double bytes, string expected)
    {
        Assert.Equal(expected, ByteFormat.Size(bytes, Invariant));
    }

    [Fact]
    public void Rate_appends_per_second_and_respects_the_culture()
    {
        Assert.Equal("1.5 MB/s", ByteFormat.Rate(1.5 * 1024 * 1024, Invariant));
        Assert.Equal("1,5 MB/s", ByteFormat.Rate(1.5 * 1024 * 1024, CultureInfo.GetCultureInfo("pt-PT")));
    }
}

public class NiceScaleTests
{
    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(99, 100, 100)]
    [InlineData(101, 100, 200)]
    [InlineData(200, 100, 200)]
    [InlineData(201, 100, 500)]
    [InlineData(999, 100, 1000)]
    [InlineData(1500, 100, 2000)]
    [InlineData(6_000_000, 100, 10_000_000)]
    [InlineData(double.NaN, 100, 100)]
    public void Rounds_up_to_one_two_or_five_times_a_power_of_ten(double value, double minimum, double expected)
    {
        Assert.Equal(expected, NiceScale.Ceiling(value, minimum), 6);
    }
}

public class SparklineTests
{
    [Fact]
    public void Newest_sample_is_pinned_to_the_right_edge()
    {
        var history = new SampleHistory(5);
        history.Add(0);
        history.Add(50);
        history.Add(100);
        var points = new (double X, double Y)[5];

        int count = Sparkline.BuildPoints(history, maximum: 100, width: 40, height: 20, points);

        Assert.Equal(3, count);
        Assert.Equal((20, 20), points[0]); // 0 % sits on the baseline, two steps from the right
        Assert.Equal((30, 10), points[1]);
        Assert.Equal((40, 0), points[2]);  // 100 % reaches the top at the right edge
    }

    [Fact]
    public void A_full_history_spans_the_whole_width_and_clamps_outliers()
    {
        var history = new SampleHistory(3);
        history.Add(10);
        history.Add(500);
        history.Add(-3);
        var points = new (double X, double Y)[3];

        int count = Sparkline.BuildPoints(history, 100, 60, 10, points);

        Assert.Equal(3, count);
        Assert.Equal(0, points[0].X);
        Assert.Equal(30, points[1].X);
        Assert.Equal(60, points[2].X);
        Assert.Equal(0, points[1].Y);   // clamped to the maximum
        Assert.Equal(10, points[2].Y);  // clamped to zero
    }

    [Fact]
    public void Degenerate_input_produces_no_points()
    {
        var history = new SampleHistory(3);
        var points = new (double X, double Y)[3];
        Assert.Equal(0, Sparkline.BuildPoints(history, 100, 60, 10, points));

        history.Add(1);
        Assert.Equal(0, Sparkline.BuildPoints(history, 100, 0, 10, points));
        Assert.Equal(1, Sparkline.BuildPoints(history, maximum: 0, 60, 10, points)); // bad maximum falls back to 1
    }
}

public class MetricsSnapshotTests
{
    [Fact]
    public void Memory_percent_is_derived_and_safe_for_zero_totals()
    {
        Assert.Equal(25, new MetricsSnapshot { MemoryUsedBytes = 4, MemoryTotalBytes = 16 }.MemoryPercent);
        Assert.Equal(0, new MetricsSnapshot().MemoryPercent);
    }
}
