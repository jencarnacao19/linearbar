using LinearBar.Core.Geometry;

namespace LinearBar.Core.Tests.Geometry;

public class SquircleTests
{
    private static void AssertClosedAndContinuous(SquirclePath path)
    {
        Assert.Equal(path.Start.X, path.Segments[^1].End.X, 6);
        Assert.Equal(path.Start.Y, path.Segments[^1].End.Y, 6);
    }

    [Theory]
    [InlineData(100, 100, 22)]
    [InlineData(300, 54, 17)]
    [InlineData(40, 120, 30)]
    [InlineData(64, 64, 32)]   // radius at the limit: a fully round "pill"
    [InlineData(64, 64, 500)]  // absurd radius is clamped
    public void Outline_is_closed_and_stays_inside_its_box(double width, double height, double radius)
    {
        SquirclePath path = Squircle.Build(10, 20, width, height, radius);

        AssertClosedAndContinuous(path);
        foreach (Vec point in path.Flatten(16))
        {
            Assert.InRange(point.X, 10 - 1e-6, 10 + width + 1e-6);
            Assert.InRange(point.Y, 20 - 1e-6, 20 + height + 1e-6);
        }
    }

    [Fact]
    public void Outline_reaches_every_edge_of_its_box()
    {
        List<Vec> points = Squircle.Build(0, 0, 200, 100, 24).Flatten(16);

        Assert.Equal(0, points.Min(p => p.X), 6);
        Assert.Equal(200, points.Max(p => p.X), 6);
        Assert.Equal(0, points.Min(p => p.Y), 6);
        Assert.Equal(100, points.Max(p => p.Y), 6);
    }

    [Fact]
    public void Shape_is_symmetric()
    {
        List<Vec> points = Squircle.Build(0, 0, 120, 120, 30).Flatten(32);

        // Every point mirrored across both axes lies (close to) on the outline too.
        foreach (Vec point in points)
        {
            var mirrored = new Vec(120 - point.X, point.Y);
            double nearest = points.Min(other => (other - mirrored).Length);
            Assert.True(nearest < 0.6, $"no mirror for {point}");
        }
    }

    [Fact]
    public void Zero_smoothing_gives_a_circular_corner()
    {
        const double radius = 30;
        List<Vec> points = Squircle.Build(0, 0, 200, 200, radius, smoothing: 0).Flatten(32);
        var centre = new Vec(200 - radius, radius);

        // Points of the top-right corner sit on the circle of the corner radius.
        foreach (Vec point in points.Where(p => p.X > 200 - radius + 1e-6 && p.Y < radius - 1e-6))
        {
            Assert.Equal(radius, (point - centre).Length, 1);
        }
    }

    [Fact]
    public void Smoothing_starts_the_curve_earlier_and_keeps_the_corner_fuller()
    {
        // With smoothing the curve begins further from the corner (p = (1 + s) * R) …
        SquirclePath round = Squircle.Build(0, 0, 200, 200, 30, smoothing: 0);
        SquirclePath smooth = Squircle.Build(0, 0, 200, 200, 30, smoothing: 0.6);
        Assert.Equal(200 - 30, round.Start.X, 6);
        Assert.Equal(200 - 48, smooth.Start.X, 6);

        // … and the diagonal of the corner is pulled closer to the box corner.
        static double DiagonalGap(SquirclePath path) => path.Flatten(64)
            .Where(p => p.X > 100 && p.Y < 100)
            .Min(p => (new Vec(200, 0) - p).Length);

        Assert.True(DiagonalGap(smooth) > DiagonalGap(round) - 3);
    }

    [Fact]
    public void Zero_radius_is_a_plain_rectangle_and_flatten_is_polygonal()
    {
        SquirclePath path = Squircle.Build(1, 2, 10, 5, 0);

        Assert.All(path.Segments, segment => Assert.False(segment.IsCurve));
        Assert.Equal([new Vec(1, 2), new Vec(11, 2), new Vec(11, 7), new Vec(1, 7), new Vec(1, 2)], path.Flatten());
    }

    [Fact]
    public void Degenerate_input_does_not_throw()
    {
        Assert.NotNull(Squircle.Build(0, 0, 0, 0, 10));
        Assert.NotNull(Squircle.Build(0, 0, -5, 10, double.NaN, double.PositiveInfinity));
    }
}
