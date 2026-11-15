using LinearBar.Core.Dock;

namespace LinearBar.Core.Tests.Dock;

public class LensTests
{
    private static readonly Lens Lens = new(Magnification: 0.75, Radius: 120);

    [Fact]
    public void Scale_peaks_under_the_cursor_and_fades_to_one_at_the_radius()
    {
        Assert.Equal(1.75, Lens.ScaleAt(0), 10);
        Assert.Equal(1.0, Lens.ScaleAt(120), 10);
        Assert.Equal(1.0, Lens.ScaleAt(500), 10);
        Assert.Equal(Lens.ScaleAt(40), Lens.ScaleAt(-40), 10);
        Assert.InRange(Lens.ScaleAt(60), 1.0, 1.75);
    }

    [Fact]
    public void Displacement_keeps_the_point_under_the_cursor_fixed()
    {
        Assert.Equal(0, Lens.Displace(0), 10);
    }

    [Fact]
    public void Displacement_is_odd_and_strictly_increasing()
    {
        double previous = Lens.Displace(-300);
        for (double d = -299; d <= 300; d += 1)
        {
            double current = Lens.Displace(d);
            Assert.True(current > previous, $"not increasing at {d}");
            Assert.Equal(-Lens.Displace(-d), current, 9);
            previous = current;
        }
    }

    [Fact]
    public void Displacement_is_the_integral_of_the_scale()
    {
        // Numerically integrate ScaleAt and compare with the closed form.
        const double step = 0.01;
        const int steps = 20_000; // 0 … 200
        double integral = 0;
        for (int i = 0; i < steps; i++)
        {
            integral += Lens.ScaleAt((i + 0.5) * step) * step;
        }

        Assert.Equal(integral, Lens.Displace(200), 3);
    }

    [Fact]
    public void Points_beyond_the_radius_shift_by_a_constant()
    {
        double k = Lens.MaxDisplacement;

        Assert.Equal(0.75 * 0.5 * 120, k, 10);
        Assert.Equal(120 + k, Lens.Displace(120), 9);
        Assert.Equal(400 + k, Lens.Displace(400), 9);
        Assert.Equal(-400 - k, Lens.Displace(-400), 9);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(0.5, 0)]
    [InlineData(-1, 100)]
    public void Inactive_lens_changes_nothing(double magnification, double radius)
    {
        var lens = new Lens(magnification, radius);

        Assert.False(lens.IsActive);
        Assert.Equal(1, lens.ScaleAt(10));
        Assert.Equal(37.5, lens.Displace(37.5));
        Assert.Equal(0, lens.MaxDisplacement);
    }
}
