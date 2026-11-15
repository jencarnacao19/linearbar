namespace LinearBar.Core.Geometry;

/// <summary>A point in layout units.</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>One piece of a <see cref="SquirclePath"/>: a straight line or a cubic Bézier curve.</summary>
/// <param name="IsCurve">True for a cubic curve; false for a line to <paramref name="End"/>.</param>
public readonly record struct PathSegment(bool IsCurve, Vec Control1, Vec Control2, Vec End)
{
    public static PathSegment Line(Vec end) => new(false, end, end, end);

    public static PathSegment Curve(Vec control1, Vec control2, Vec end) => new(true, control1, control2, end);
}

/// <summary>A closed outline made of lines and cubic curves, starting at <see cref="Start"/>.</summary>
public sealed record SquirclePath(Vec Start, IReadOnlyList<PathSegment> Segments)
{
    /// <summary>
    /// Approximates the outline with a polygon, e.g. for a Win32 window region.
    /// Curves are split into <paramref name="stepsPerCurve"/> straight pieces.
    /// </summary>
    public List<Vec> Flatten(int stepsPerCurve = 8)
    {
        var points = new List<Vec> { Start };
        Vec current = Start;

        foreach (PathSegment segment in Segments)
        {
            if (!segment.IsCurve)
            {
                points.Add(segment.End);
            }
            else
            {
                for (int i = 1; i <= stepsPerCurve; i++)
                {
                    points.Add(Bezier(current, segment.Control1, segment.Control2, segment.End, (double)i / stepsPerCurve));
                }
            }

            current = segment.End;
        }

        return points;
    }

    private static Vec Bezier(Vec p0, Vec p1, Vec p2, Vec p3, double t)
    {
        double u = 1 - t;
        return p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t);
    }
}

/// <summary>
/// Rounded rectangles with continuous ("squircle") corners, as in iOS, macOS and Figma's
/// corner smoothing: the curvature ramps up gradually instead of jumping from a straight
/// edge to a circular arc, which is what makes ordinary rounded corners look slightly off.
/// </summary>
/// <remarks>
/// Port of the corner-smoothing construction from Figma's article "Desperately seeking
/// squircles" (https://www.figma.com/blog/desperately-seeking-squircles/), following the
/// MIT-licensed implementation in phamfoo/figma-squircle
/// (https://github.com/phamfoo/figma-squircle), with <c>preserveSmoothing</c> enabled.
/// Each corner is a cubic Bézier, a short circular arc and another cubic Bézier; the arc is
/// converted to a cubic here so the whole outline is lines and cubics only.
/// </remarks>
public static class Squircle
{
    /// <summary>Corner smoothing that matches Apple's app icons and controls.</summary>
    public const double AppleSmoothing = 0.6;

    /// <summary>Builds the outline of a <paramref name="width"/> × <paramref name="height"/> squircle at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="radius">Corner radius, as for an ordinary rounded rectangle.</param>
    /// <param name="smoothing">0 gives ordinary circular corners; 1 the smoothest transition.</param>
    public static SquirclePath Build(double x, double y, double width, double height, double radius, double smoothing = AppleSmoothing)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        double budget = Math.Min(width, height) / 2;
        radius = Math.Clamp(double.IsFinite(radius) ? radius : 0, 0, budget);
        smoothing = Math.Clamp(double.IsFinite(smoothing) ? smoothing : 0, 0, 1);

        if (radius <= 0)
        {
            return new SquirclePath(new Vec(x, y), [
                PathSegment.Line(new Vec(x + width, y)),
                PathSegment.Line(new Vec(x + width, y + height)),
                PathSegment.Line(new Vec(x, y + height)),
                PathSegment.Line(new Vec(x, y)),
            ]);
        }

        Corner k = Corner.Compute(radius, smoothing, budget);
        var segments = new List<PathSegment>(16);

        // Start on the top edge and go clockwise (y grows downwards). Every corner is the
        // same shape rotated, so describe it once in a local frame — "along" runs with the
        // edge we arrive on, "across" turns towards the next edge — and map it per corner.
        var start = new Vec(x + width - k.P, y);
        Vec cursor = start;

        AddCorner(segments, ref cursor, k, along: new Vec(1, 0), across: new Vec(0, 1));    // top-right
        AddLine(segments, ref cursor, new Vec(x + width, y + height - k.P));
        AddCorner(segments, ref cursor, k, along: new Vec(0, 1), across: new Vec(-1, 0));   // bottom-right
        AddLine(segments, ref cursor, new Vec(x + k.P, y + height));
        AddCorner(segments, ref cursor, k, along: new Vec(-1, 0), across: new Vec(0, -1));  // bottom-left
        AddLine(segments, ref cursor, new Vec(x, y + k.P));
        AddCorner(segments, ref cursor, k, along: new Vec(0, -1), across: new Vec(1, 0));   // top-left
        AddLine(segments, ref cursor, start);

        return new SquirclePath(start, segments);
    }

    private static void AddLine(List<PathSegment> segments, ref Vec cursor, Vec end)
    {
        if ((end - cursor).Length > 1e-9)
        {
            segments.Add(PathSegment.Line(end));
        }

        cursor = end;
    }

    private static void AddCorner(List<PathSegment> segments, ref Vec cursor, Corner k, Vec along, Vec across)
    {
        Vec Local(double u, double v) => along * u + across * v;

        // Entry curve: "c a 0, a+b 0, a+b+c d" in figma-squircle's top-right corner.
        Vec origin = cursor;
        Vec arcStart = origin + Local(k.A + k.B + k.C, k.D);
        segments.Add(PathSegment.Curve(origin + Local(k.A, 0), origin + Local(k.A + k.B, 0), arcStart));

        // Arc: "a R R 0 0 1 L L" — a clockwise circular arc of radius R.
        Vec arcEnd = arcStart + Local(k.ArcLength, k.ArcLength);
        AddArc(segments, arcStart, arcEnd, k.Radius);

        // Exit curve: "c d c, d b+c, d a+b+c".
        Vec end = arcEnd + Local(k.D, k.A + k.B + k.C);
        segments.Add(PathSegment.Curve(arcEnd + Local(k.D, k.C), arcEnd + Local(k.D, k.B + k.C), end));

        cursor = end;
    }

    /// <summary>Appends a short clockwise (screen coordinates) circular arc as one cubic.</summary>
    private static void AddArc(List<PathSegment> segments, Vec from, Vec to, double radius)
    {
        Vec chord = to - from;
        double chordLength = chord.Length;
        if (chordLength < 1e-9)
        {
            return;
        }

        // Centre: on the perpendicular bisector, on the side that makes the arc turn
        // clockwise on screen (to the right of the direction of travel).
        Vec mid = (from + to) * 0.5;
        double half = chordLength / 2;
        double h = Math.Sqrt(Math.Max(0, radius * radius - half * half));
        var right = new Vec(-chord.Y / chordLength, chord.X / chordLength);
        Vec centre = mid + right * h;

        double a0 = Math.Atan2(from.Y - centre.Y, from.X - centre.X);
        double a1 = Math.Atan2(to.Y - centre.Y, to.X - centre.X);
        double sweep = a1 - a0;
        while (sweep <= -Math.PI) sweep += 2 * Math.PI;
        while (sweep > Math.PI) sweep -= 2 * Math.PI;

        // Standard cubic approximation of a circular arc.
        double handle = 4.0 / 3.0 * Math.Tan(sweep / 4) * radius;
        var t0 = new Vec(-Math.Sin(a0), Math.Cos(a0));
        var t1 = new Vec(-Math.Sin(a1), Math.Cos(a1));
        segments.Add(PathSegment.Curve(from + t0 * handle, to - t1 * handle, to));
    }

    /// <summary>Per-corner construction values (figure 11.1 of the Figma article).</summary>
    private readonly record struct Corner(double A, double B, double C, double D, double P, double Radius, double ArcLength)
    {
        public static Corner Compute(double radius, double smoothing, double budget)
        {
            double p = (1 + smoothing) * radius;

            double arcMeasure = 90 * (1 - smoothing);
            double arcLength = Math.Sin(ToRadians(arcMeasure / 2)) * radius * Math.Sqrt(2);

            double angleAlpha = (90 - arcMeasure) / 2;
            double p3ToP4 = radius * Math.Tan(ToRadians(angleAlpha / 2));

            double angleBeta = 45 * smoothing;
            double c = p3ToP4 * Math.Cos(ToRadians(angleBeta));
            double d = c * Math.Tan(ToRadians(angleBeta));

            double b = (p - arcLength - c - d) / 3;
            double a = 2 * b;

            // preserveSmoothing: when the corner does not fit, keep the smoothing and squeeze
            // the first two control points instead.
            if (p > budget)
            {
                double p1ToP3Max = budget - d - arcLength - c;
                double minA = p1ToP3Max / 6;
                double maxB = p1ToP3Max - minA;
                b = Math.Min(b, maxB);
                a = p1ToP3Max - b;
                p = Math.Min(p, budget);
            }

            return new Corner(a, b, c, d, p, radius, arcLength);
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
