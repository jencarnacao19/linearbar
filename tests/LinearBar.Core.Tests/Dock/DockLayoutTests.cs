using LinearBar.Core.Dock;

namespace LinearBar.Core.Tests.Dock;

public class DockLayoutTests
{
    private const double Slot = 48;
    private const double Origin = 400;
    private static readonly Lens Lens = new(0.75, 2.5 * Slot);

    [Fact]
    public void Without_a_cursor_slots_rest_side_by_side()
    {
        var slots = new SlotPlacement[4];

        DockLayout.Arrange(slots, Origin, Slot, double.NaN, Lens);

        for (int i = 0; i < slots.Length; i++)
        {
            Assert.Equal(Origin + (i + 0.5) * Slot, slots[i].Center, 9);
            Assert.Equal(1, slots[i].Scale, 9);
        }
    }

    [Fact]
    public void Magnified_slots_stay_contiguous_and_ordered()
    {
        var slots = new SlotPlacement[12];

        for (double cursor = Origin - 100; cursor <= Origin + 12 * Slot + 100; cursor += 7)
        {
            DockLayout.Arrange(slots, Origin, Slot, cursor, Lens);

            for (int i = 1; i < slots.Length; i++)
            {
                double previousRight = slots[i - 1].Center + slots[i - 1].Scale * Slot / 2;
                double left = slots[i].Center - slots[i].Scale * Slot / 2;
                Assert.Equal(previousRight, left, 6);
                Assert.True(slots[i].Center > slots[i - 1].Center);
            }
        }
    }

    [Fact]
    public void Slot_under_the_cursor_is_the_largest_and_stays_under_the_cursor()
    {
        var slots = new SlotPlacement[9];
        double cursor = Origin + 4.5 * Slot; // centre of slot 4

        DockLayout.Arrange(slots, Origin, Slot, cursor, Lens);

        Assert.Equal(cursor, slots[4].Center, 9);
        Assert.Equal(slots.Max(s => s.Scale), slots[4].Scale, 9);
        Assert.InRange(slots[4].Scale, 1.6, 1.75);

        // Symmetric neighbours get the same size.
        Assert.Equal(slots[3].Scale, slots[5].Scale, 9);
        Assert.True(slots[3].Scale < slots[4].Scale);
        Assert.Equal(1, slots[0].Scale, 9);
    }

    [Fact]
    public void Dock_width_is_constant_while_the_cursor_is_in_the_interior()
    {
        const int count = 16;
        double k = Lens.MaxDisplacement;

        for (double cursor = Origin + Lens.Radius; cursor <= Origin + count * Slot - Lens.Radius; cursor += 11)
        {
            (double left, double right) = DockLayout.Extent(count, Origin, Slot, cursor, Lens);

            Assert.Equal(Origin - k, left, 9);
            Assert.Equal(Origin + count * Slot + k, right, 9);
        }
    }

    [Fact]
    public void Extent_matches_the_arranged_slots()
    {
        var slots = new SlotPlacement[6];
        double cursor = Origin + 30;

        DockLayout.Arrange(slots, Origin, Slot, cursor, Lens);
        (double left, double right) = DockLayout.Extent(slots.Length, Origin, Slot, cursor, Lens);

        Assert.Equal(slots[0].Center - slots[0].Scale * Slot / 2, left, 9);
        Assert.Equal(slots[^1].Center + slots[^1].Scale * Slot / 2, right, 9);
    }

    [Theory]
    [InlineData(399.9, -1)]
    [InlineData(400, 0)]
    [InlineData(447.9, 0)]
    [InlineData(448, 1)]
    [InlineData(591.9, 3)]
    [InlineData(592, -1)]
    [InlineData(double.NaN, -1)]
    public void HitTest_uses_the_resting_layout(double x, int expected)
    {
        Assert.Equal(expected, DockLayout.HitTest(4, Origin, Slot, x));
    }

    [Fact]
    public void FitScale_shrinks_only_when_the_dock_does_not_fit()
    {
        Assert.Equal(1, DockLayout.FitScale(10, 48, 45, 1000));
        Assert.Equal(1, DockLayout.FitScale(0, 48, 45, 10));

        double fit = DockLayout.FitScale(30, 48, 45, 1000);
        Assert.Equal(1000.0 / (30 * 48 + 90), fit, 9);

        // Never below the minimum, however crowded.
        Assert.Equal(0.4, DockLayout.FitScale(500, 48, 45, 1000));
    }
}
