using LinearBar.Core.Config;

namespace LinearBar.Core.Dock;

/// <summary>
/// All sizes of the bar and dock, derived from the settings in one place. Units are
/// device-independent pixels.
/// </summary>
/// <remarks>
/// <para>
/// A dock <em>cell</em> is the icon plus the strip underneath it that holds the running
/// indicator. Cells are scaled as a whole around their bottom-centre, so they grow upwards.
/// </para>
/// <para>
/// Only <see cref="BarHeight"/> is reserved on screen. The window is taller by
/// <see cref="OverflowHeight"/>: a transparent, click-through band above the bar into which
/// magnified icons and their name labels extend.
/// </para>
/// </remarks>
public readonly record struct BarGeometry
{
    /// <summary>Height of the strip under each icon that holds the running indicator.</summary>
    public const double IndicatorSpace = 7;

    /// <summary>Room reserved above the tallest icon for its name label.</summary>
    public const double LabelSpace = 34;

    /// <summary>Height of the visible bar strip; the screen space reserved.</summary>
    public double BarHeight { get; init; }

    /// <summary>Resting icon size.</summary>
    public double IconSize { get; init; }

    /// <summary>Resting width of one dock slot (icon plus spacing).</summary>
    public double SlotWidth { get; init; }

    /// <summary>Resting height of one dock cell (icon plus indicator strip).</summary>
    public double CellHeight { get; init; }

    /// <summary>Distance from the bottom of the bar to the bottom of a cell.</summary>
    public double CellBottomMargin { get; init; }

    /// <summary>Peak scale of the lens (1 when magnification is off).</summary>
    public double MaxScale { get; init; }

    /// <summary>Lens radius in device-independent pixels.</summary>
    public double LensRadius { get; init; }

    /// <summary>Height of the transparent band above the bar.</summary>
    public double OverflowHeight { get; init; }

    /// <summary>Total window height.</summary>
    public double WindowHeight => BarHeight + OverflowHeight;

    /// <summary>Lens at full strength.</summary>
    public Lens Lens => new(MaxScale - 1, LensRadius);

    public static BarGeometry From(BarSettings settings)
    {
        DockSettings dock = settings.Dock;

        double barHeight = settings.BarHeight;
        double iconSize = dock.IconSize;
        double slotWidth = iconSize + dock.Spacing;
        double cellHeight = iconSize + IndicatorSpace;
        double bottomMargin = Math.Max(0, Math.Floor((barHeight - cellHeight) / 2));
        double maxScale = dock.Magnify ? Math.Max(1, dock.MaxScale) : 1;

        double tallestCell = cellHeight * maxScale + bottomMargin;
        double overflow = Math.Max(0, tallestCell - barHeight);
        if (dock.ShowLabels)
        {
            overflow += LabelSpace;
        }

        return new BarGeometry
        {
            BarHeight = barHeight,
            IconSize = iconSize,
            SlotWidth = slotWidth,
            CellHeight = cellHeight,
            CellBottomMargin = bottomMargin,
            MaxScale = maxScale,
            LensRadius = dock.LensRadius * slotWidth,
            OverflowHeight = Math.Ceiling(overflow),
        };
    }
}
