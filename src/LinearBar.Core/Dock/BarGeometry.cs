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

    /// <summary>Default gap between the floating glass islands and the screen edges.</summary>
    public const double DefaultIslandMargin = 5;

    /// <summary>Minimum room between the top of a resting icon and the top of the dock platter.</summary>
    public const double PlatterTopPadding = 5;

    /// <summary>Distance from the bottom of the platter to the bottom of the dock cells.</summary>
    public const double PlatterBottomPadding = 2;

    /// <summary>Extra platter width beyond the outer slots, on each side.</summary>
    public const double PlatterSidePadding = 6;

    /// <summary>Corner radius of an app icon tile relative to its size (Apple's app icon ratio).</summary>
    public const double TileRadiusRatio = 0.2237;

    /// <summary>Largest icon that fits a bar of <paramref name="barHeight"/> with the platter padding.</summary>
    public static double MaxIconSize(double barHeight, double islandMargin = DefaultIslandMargin) =>
        barHeight - 2 * islandMargin - PlatterTopPadding - PlatterBottomPadding - IndicatorSpace;

    /// <summary>Gap between the islands and the screen edges.</summary>
    public double IslandMargin { get; init; }

    /// <summary>Island corner radius as a fraction of the island height.</summary>
    public double Roundness { get; init; }

    /// <summary>True when the bar is at the top of the screen and icons grow downwards.</summary>
    public bool IsTop { get; init; }

    /// <summary>Height of the reserved bar area; the islands float inside it.</summary>
    public double BarHeight { get; init; }

    /// <summary>Resting icon size.</summary>
    public double IconSize { get; init; }

    /// <summary>Resting width of one dock slot (icon plus spacing).</summary>
    public double SlotWidth { get; init; }

    /// <summary>Resting height of one dock cell (icon plus indicator strip).</summary>
    public double CellHeight { get; init; }

    /// <summary>Distance from the bottom of the bar to the bottom of a cell.</summary>
    public double CellBottomMargin { get; init; }

    /// <summary>Height of the glass islands (dock platter, Start, widgets).</summary>
    public double IslandHeight => BarHeight - 2 * IslandMargin;

    /// <summary>Corner radius of the islands.</summary>
    public double IslandRadius => Math.Round(IslandHeight * Roundness);

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
        double margin = settings.Appearance.IslandMargin;
        double iconSize = Math.Min(dock.IconSize, Math.Max(16, MaxIconSize(barHeight, margin)));
        double slotWidth = iconSize + dock.Spacing;
        double cellHeight = iconSize + IndicatorSpace;
        double bottomMargin = margin + PlatterBottomPadding;
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
            IslandMargin = margin,
            Roundness = settings.Appearance.CornerRoundness,
            IsTop = settings.Layout.IsTop,
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
