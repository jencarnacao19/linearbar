namespace LinearBar.Core.Dock;

/// <summary>
/// One-dimensional fisheye lens that drives the macOS-style dock magnification.
/// </summary>
/// <remarks>
/// <para>
/// The lens is centred on the cursor. A point at signed distance <c>d</c> from the cursor is
/// locally scaled by <see cref="ScaleAt"/> — a raised cosine that peaks at
/// <c>1 + Magnification</c> under the cursor and falls to exactly 1 at <see cref="Radius"/>.
/// </para>
/// <para>
/// Positions are not scaled independently: every point is moved by <see cref="Displace"/>,
/// the integral of the scale function. Mapping slot edges through that single monotonic
/// function is what gives the dock its properties:
/// </para>
/// <list type="bullet">
/// <item>icons can never overlap or leave gaps (monotonic map of contiguous intervals);</item>
/// <item>the point under the cursor stays under the cursor (<c>Displace(0) == 0</c>), so the
/// cursor crosses icons at the un-magnified pitch, exactly like the macOS dock;</item>
/// <item>everything further than <see cref="Radius"/> from the cursor is shifted by the
/// constant <see cref="MaxDisplacement"/>, so the dock ends do not jitter while the cursor
/// moves through the interior.</item>
/// </list>
/// </remarks>
/// <param name="Magnification">Extra scale under the cursor (peak scale minus one). 0 disables the lens.</param>
/// <param name="Radius">Distance from the cursor at which magnification fades out, in layout units.</param>
public readonly record struct Lens(double Magnification, double Radius)
{
    /// <summary>A lens that leaves everything untouched.</summary>
    public static Lens None => new(0, 0);

    /// <summary>True when the lens changes anything at all.</summary>
    public bool IsActive => Magnification > 0 && Radius > 0;

    /// <summary>Constant shift applied to every point further than <see cref="Radius"/> from the cursor.</summary>
    public double MaxDisplacement => IsActive ? Magnification * 0.5 * Radius : 0;

    /// <summary>Local scale factor at signed distance <paramref name="distance"/> from the cursor.</summary>
    public double ScaleAt(double distance)
    {
        if (!IsActive)
        {
            return 1;
        }

        double d = Math.Abs(distance);
        if (d >= Radius)
        {
            return 1;
        }

        return 1 + Magnification * 0.5 * (1 + Math.Cos(Math.PI * d / Radius));
    }

    /// <summary>
    /// Maps a signed distance from the cursor in the resting layout to the signed distance in
    /// the magnified layout. This is the integral of <see cref="ScaleAt"/> from 0 to
    /// <paramref name="distance"/>; it is odd, continuous and strictly increasing.
    /// </summary>
    public double Displace(double distance)
    {
        if (!IsActive)
        {
            return distance;
        }

        double d = Math.Abs(distance);
        double extra = d >= Radius
            ? Magnification * 0.5 * Radius
            : Magnification * 0.5 * (d + Radius / Math.PI * Math.Sin(Math.PI * d / Radius));

        return distance < 0 ? -(d + extra) : d + extra;
    }
}
