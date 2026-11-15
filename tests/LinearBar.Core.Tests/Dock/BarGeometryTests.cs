using LinearBar.Core.Animation;
using LinearBar.Core.Config;
using LinearBar.Core.Dock;

namespace LinearBar.Core.Tests.Dock;

public class BarGeometryTests
{
    [Fact]
    public void Default_settings_give_a_floating_dock_with_room_for_magnified_icons_and_labels()
    {
        var settings = new BarSettings();
        settings.Normalize();

        BarGeometry geometry = BarGeometry.From(settings);

        Assert.Equal(64, geometry.BarHeight);
        Assert.Equal(40, geometry.IconSize);
        Assert.Equal(48, geometry.SlotWidth);
        Assert.Equal(47, geometry.CellHeight);
        Assert.Equal(7, geometry.CellBottomMargin);
        Assert.Equal(54, geometry.IslandHeight);
        Assert.Equal(19, geometry.IslandRadius);
        Assert.Equal(new Lens(0.75, 120), geometry.Lens);

        // The tallest cell (47 × 1.75 + 7 = 89.25) sticks out 25.25 above the 64 px bar; add the label.
        Assert.Equal(Math.Ceiling(25.25 + BarGeometry.LabelSpace), geometry.OverflowHeight);
        Assert.Equal(geometry.BarHeight + geometry.OverflowHeight, geometry.WindowHeight);
    }

    [Fact]
    public void Without_magnification_or_labels_the_window_is_exactly_the_bar()
    {
        var settings = new BarSettings();
        settings.Dock.Magnify = false;
        settings.Dock.ShowLabels = false;
        settings.Normalize();

        BarGeometry geometry = BarGeometry.From(settings);

        Assert.Equal(1, geometry.MaxScale);
        Assert.False(geometry.Lens.IsActive);
        Assert.Equal(0, geometry.OverflowHeight);
        Assert.Equal(geometry.BarHeight, geometry.WindowHeight);
    }

    [Fact]
    public void A_resting_cell_always_fits_inside_the_bar()
    {
        for (double barHeight = 40; barHeight <= 110; barHeight += 2)
        {
            var settings = new BarSettings { BarHeight = barHeight };
            settings.Dock.IconSize = 500; // absurd on purpose; Normalize must rein it in
            settings.Normalize();

            BarGeometry geometry = BarGeometry.From(settings);

            double platterTop = geometry.BarHeight - BarGeometry.IslandMargin;
            Assert.True(geometry.CellBottomMargin + geometry.CellHeight + BarGeometry.PlatterTopPadding <= platterTop + 1e-9, $"bar {barHeight}");
        }
    }
}

public class SmoothDampTests
{
    [Fact]
    public void Converges_on_the_target_without_overshooting()
    {
        var spring = new SmoothDamp(0) { Target = 1 };
        double previous = 0;
        int steps = 0;

        while (spring.Step(1 / 60.0, 0.12))
        {
            Assert.InRange(spring.Value, previous, 1.0);
            previous = spring.Value;
            Assert.True(++steps < 600, "did not settle");
        }

        Assert.Equal(1, spring.Value);
        Assert.True(spring.IsSettled);
        Assert.InRange(steps, 5, 120);
    }

    [Fact]
    public void Is_frame_rate_independent()
    {
        var fast = new SmoothDamp(0) { Target = 1 };
        var slow = new SmoothDamp(0) { Target = 1 };

        for (int i = 0; i < 24; i++) fast.Step(1 / 240.0, 0.12);
        for (int i = 0; i < 3; i++) slow.Step(1 / 30.0, 0.12);

        Assert.Equal(fast.Value, slow.Value, 1);
    }

    [Fact]
    public void Follows_a_target_that_changes_direction()
    {
        var spring = new SmoothDamp(0) { Target = 1 };
        for (int i = 0; i < 6; i++) spring.Step(1 / 60.0, 0.12);
        double reached = spring.Value;
        Assert.InRange(reached, 0.05, 0.95);

        spring.Target = 0;
        while (spring.Step(1 / 60.0, 0.12))
        {
            Assert.InRange(spring.Value, 0, 1);
        }

        Assert.Equal(0, spring.Value);
    }

    [Fact]
    public void Settled_spring_does_no_work_and_snap_jumps()
    {
        var spring = new SmoothDamp(0.5);
        Assert.False(spring.Step(0.016, 0.12));
        Assert.Equal(0.5, spring.Value);

        spring.Target = 2;
        Assert.False(spring.Step(0.016, smoothTime: 0)); // zero smooth time means "no animation"
        Assert.Equal(2, spring.Value);

        spring.Snap(7);
        Assert.Equal(7, spring.Value);
        Assert.True(spring.IsSettled);
    }
}
