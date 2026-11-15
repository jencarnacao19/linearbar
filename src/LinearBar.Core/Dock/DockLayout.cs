namespace LinearBar.Core.Dock;

/// <summary>Where a dock slot ends up once the lens is applied.</summary>
/// <param name="Center">Horizontal centre of the slot, in the same coordinate space as the inputs.</param>
/// <param name="Scale">Size of the slot relative to its resting size (1 = not magnified).</param>
public readonly record struct SlotPlacement(double Center, double Scale);

/// <summary>
/// Pure layout maths for a row of equally sized dock slots. A slot is the icon plus the gap
/// that belongs to it, so slots are contiguous and tile the dock with no space between them.
/// </summary>
public static class DockLayout
{
    /// <summary>
    /// Computes the placement of every slot.
    /// </summary>
    /// <param name="slots">Receives one placement per slot; its length is the slot count.</param>
    /// <param name="origin">Left edge of the first slot in the resting layout.</param>
    /// <param name="slotWidth">Resting width of each slot.</param>
    /// <param name="cursor">Cursor position in the same space, or <see cref="double.NaN"/> when there is none.</param>
    /// <param name="lens">Lens to apply; an inactive lens yields the resting layout.</param>
    public static void Arrange(Span<SlotPlacement> slots, double origin, double slotWidth, double cursor, Lens lens)
    {
        bool magnify = lens.IsActive && !double.IsNaN(cursor) && slotWidth > 0;

        for (int i = 0; i < slots.Length; i++)
        {
            double left = origin + i * slotWidth;
            double right = left + slotWidth;

            if (!magnify)
            {
                slots[i] = new SlotPlacement((left + right) * 0.5, 1);
                continue;
            }

            double mappedLeft = cursor + lens.Displace(left - cursor);
            double mappedRight = cursor + lens.Displace(right - cursor);
            slots[i] = new SlotPlacement((mappedLeft + mappedRight) * 0.5, (mappedRight - mappedLeft) / slotWidth);
        }
    }

    /// <summary>
    /// Left and right edges of the whole dock once the lens is applied.
    /// </summary>
    public static (double Left, double Right) Extent(int count, double origin, double slotWidth, double cursor, Lens lens)
    {
        double left = origin;
        double right = origin + Math.Max(0, count) * slotWidth;

        if (!lens.IsActive || double.IsNaN(cursor))
        {
            return (left, right);
        }

        return (cursor + lens.Displace(left - cursor), cursor + lens.Displace(right - cursor));
    }

    /// <summary>
    /// Index of the slot under <paramref name="x"/>, or -1 when it is outside the dock.
    /// Because the lens keeps the point under the cursor fixed, the cursor's position in the
    /// magnified layout equals its position in the resting layout, so hit-testing the cursor
    /// only needs the resting geometry.
    /// </summary>
    public static int HitTest(int count, double origin, double slotWidth, double x)
    {
        if (count <= 0 || slotWidth <= 0 || double.IsNaN(x))
        {
            return -1;
        }

        double offset = x - origin;
        if (offset < 0)
        {
            return -1;
        }

        int index = (int)Math.Floor(offset / slotWidth);
        return index < count ? index : -1;
    }

    /// <summary>
    /// Uniform shrink factor (at most 1) that makes <paramref name="count"/> slots, plus the
    /// room the lens needs on both sides, fit into <paramref name="availableWidth"/>.
    /// </summary>
    /// <param name="count">Number of slots.</param>
    /// <param name="slotWidth">Resting slot width before shrinking.</param>
    /// <param name="lensMargin">Extra width the lens adds on each side at full size (<see cref="Lens.MaxDisplacement"/>).</param>
    /// <param name="availableWidth">Width the dock may occupy.</param>
    /// <param name="minimum">Lower bound for the factor so icons never become unusably small.</param>
    public static double FitScale(int count, double slotWidth, double lensMargin, double availableWidth, double minimum = 0.4)
    {
        if (count <= 0 || slotWidth <= 0)
        {
            return 1;
        }

        double needed = count * slotWidth + 2 * Math.Max(0, lensMargin);
        if (availableWidth >= needed || needed <= 0)
        {
            return 1;
        }

        return Math.Clamp(availableWidth / needed, minimum, 1);
    }
}
