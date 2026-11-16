using System.Windows;
using System.Windows.Media;
using LinearBar.Core.Geometry;

namespace LinearBar.Visuals;

/// <summary>
/// Paints LinearBar's frosted-glass surfaces: the floating islands, the dock platter, the
/// name label and the app icon tiles. Every surface is a squircle (see <see cref="Squircle"/>).
/// </summary>
/// <remarks>
/// The look is built from a few cheap layers, all with frozen brushes: a soft shadow, a dark
/// tint (over the real blur provided by the backdrop window, when enabled), a faint white
/// gradient, a sheen on the upper half and a rim that is brighter at the top and catches a
/// little light at the bottom edge too.
/// </remarks>
internal static class Glass
{
    private static readonly Brush[] ShadowBrushes =
    [
        Freeze(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0))),
        Freeze(new SolidColorBrush(Color.FromArgb(14, 0, 0, 0))),
        Freeze(new SolidColorBrush(Color.FromArgb(8, 0, 0, 0))),
    ];

    private static readonly Brush Wash = Freeze(Vertical((0, Color.FromArgb(30, 255, 255, 255)), (1, Color.FromArgb(6, 255, 255, 255))));

    private static readonly Brush Sheen = Freeze(Vertical(
        (0, Color.FromArgb(34, 255, 255, 255)),
        (0.42, Color.FromArgb(0, 255, 255, 255)),
        (1, Color.FromArgb(0, 255, 255, 255))));

    private static readonly Pen Rim = Freeze(new Pen(
        Freeze(Vertical(
            (0, Color.FromArgb(120, 255, 255, 255)),
            (0.3, Color.FromArgb(30, 255, 255, 255)),
            (0.75, Color.FromArgb(14, 255, 255, 255)),
            (1, Color.FromArgb(50, 255, 255, 255)))),
        1));

    private static readonly Pen TileRim = Freeze(new Pen(
        Freeze(Vertical(
            (0, Color.FromArgb(150, 255, 255, 255)),
            (0.4, Color.FromArgb(40, 255, 255, 255)),
            (1, Color.FromArgb(70, 255, 255, 255)))),
        1));

    private static Color _tint = Color.FromRgb(16, 16, 22);
    private static readonly Color NeutralTile = Color.FromRgb(150, 156, 172);

    private static Brush _base = CreateBase(true, 0.5);
    private static readonly Brush SolidBase = Freeze(new SolidColorBrush(Color.FromArgb(238, 26, 26, 32)));

    /// <summary>Corner smoothing of every squircle (0 = plain rounded corners).</summary>
    public static double Smoothing { get; private set; } = Squircle.AppleSmoothing;

    /// <summary>True when a real blur is drawn behind the glass (see <c>BackdropWindow</c>).</summary>
    public static bool BlurBehind { get; private set; } = true;

    /// <summary>Raised when the glass parameters change, so surfaces can repaint.</summary>
    public static event Action? Changed;

    /// <summary>Applies the appearance settings.</summary>
    /// <param name="blurBehind">Whether the backdrop blur is active.</param>
    /// <param name="tintStrength">0.2–1: how dark and opaque the glass is.</param>
    /// <param name="smoothing">Squircle corner smoothing.</param>
    public static void Configure(bool blurBehind, double tintStrength, double smoothing, Color? tint = null)
    {
        BlurBehind = blurBehind;
        Smoothing = smoothing;
        _tint = tint ?? _tint;
        _base = CreateBase(blurBehind, tintStrength);
        SquircleGeometry.ClearCache();
        Changed?.Invoke();
    }

    /// <summary>Draws a glass island filling <paramref name="bounds"/>.</summary>
    /// <param name="solid">Opaque glass for pop-ups, which have no blur behind them.</param>
    public static void DrawSurface(DrawingContext dc, Rect bounds, double radius, bool shadow = true, bool solid = false)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        if (shadow)
        {
            for (int i = 0; i < ShadowBrushes.Length; i++)
            {
                double grow = i + 1;
                Rect ring = new(bounds.X - grow, bounds.Y - grow + 1.5, bounds.Width + 2 * grow, bounds.Height + 2 * grow);
                dc.DrawGeometry(ShadowBrushes[i], null, SquircleGeometry.Create(ring, radius + grow, Smoothing));
            }
        }

        Geometry shape = SquircleGeometry.Create(bounds, radius, Smoothing);
        dc.DrawGeometry(solid ? SolidBase : _base, null, shape);
        dc.DrawGeometry(Wash, null, shape);
        dc.DrawGeometry(Sheen, null, shape);

        // Half a pixel in, so the 1 px rim lands on whole pixels.
        Rect inner = new(bounds.X + 0.5, bounds.Y + 0.5, bounds.Width - 1, bounds.Height - 1);
        dc.DrawGeometry(null, Rim, SquircleGeometry.Create(inner, Math.Max(0, radius - 0.5), Smoothing));
    }

    /// <summary>Draws a frosted app icon tile, lightly tinted with the icon's colour.</summary>
    public static void DrawTile(DrawingContext dc, Rect bounds, Color? tint)
    {
        double radius = bounds.Width * Core.Dock.BarGeometry.TileRadiusRatio;
        Geometry shape = SquircleGeometry.Create(bounds, radius, Smoothing);
        Color colour = tint ?? NeutralTile;

        var fill = Vertical(
            (0, Mix(colour, Colors.White, 0.55, alpha: 120)),
            (1, Mix(colour, Colors.White, 0.15, alpha: 82)));
        fill.Freeze();

        dc.DrawGeometry(fill, null, shape);
        dc.DrawGeometry(Sheen, null, shape);

        Rect inner = new(bounds.X + 0.5, bounds.Y + 0.5, bounds.Width - 1, bounds.Height - 1);
        dc.DrawGeometry(null, TileRim, SquircleGeometry.Create(inner, Math.Max(0, radius - 0.5), Smoothing));
    }

    private static Brush CreateBase(bool blurBehind, double tintStrength)
    {
        // Over a real blur the tint can be light; without it, it must hide what is behind.
        double alpha = blurBehind ? 0.25 + 0.55 * tintStrength : 0.62 + 0.33 * tintStrength;
        return Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), _tint.R, _tint.G, _tint.B)));
    }

    private static LinearGradientBrush Vertical(params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach ((double offset, Color color) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    private static Color Mix(Color a, Color b, double amountOfB, byte alpha)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * amountOfB);
        return Color.FromArgb(alpha, Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
