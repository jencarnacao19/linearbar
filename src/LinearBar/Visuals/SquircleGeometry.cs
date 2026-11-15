using System.Windows;
using System.Windows.Media;
using LinearBar.Core.Geometry;

namespace LinearBar.Visuals;

/// <summary>Turns <see cref="Squircle"/> outlines into frozen WPF geometry, with a small cache.</summary>
internal static class SquircleGeometry
{
    private const int MaxCacheEntries = 256;

    private static readonly Dictionary<(double X, double Y, double W, double H, double R, double S), Geometry> Cache = [];

    public static Geometry Create(Rect bounds, double radius, double smoothing)
    {
        var key = (Round(bounds.X), Round(bounds.Y), Round(bounds.Width), Round(bounds.Height), Round(radius), Round(smoothing));
        if (Cache.TryGetValue(key, out Geometry? cached))
        {
            return cached;
        }

        SquirclePath path = Squircle.Build(bounds.X, bounds.Y, bounds.Width, bounds.Height, radius, smoothing);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(ToPoint(path.Start), isFilled: true, isClosed: true);
            foreach (Core.Geometry.PathSegment segment in path.Segments)
            {
                if (segment.IsCurve)
                {
                    context.BezierTo(ToPoint(segment.Control1), ToPoint(segment.Control2), ToPoint(segment.End), isStroked: true, isSmoothJoin: true);
                }
                else
                {
                    context.LineTo(ToPoint(segment.End), isStroked: true, isSmoothJoin: true);
                }
            }
        }

        geometry.Freeze();

        // Animated shapes (the dock platter) produce a new size every frame; keep the cache bounded.
        if (Cache.Count >= MaxCacheEntries)
        {
            Cache.Clear();
        }

        Cache[key] = geometry;
        return geometry;
    }

    public static void ClearCache() => Cache.Clear();

    private static double Round(double value) => Math.Round(value, 2);

    private static Point ToPoint(Vec v) => new(v.X, v.Y);
}
