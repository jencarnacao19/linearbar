using System.Collections.ObjectModel;
using System.Collections.Specialized;
using LinearBar.Core.Dock;

namespace LinearBar.Core.Tests.Dock;

public class DockPlannerTests
{
    [Fact]
    public void Pinned_apps_come_first_in_pin_order_then_running_apps_by_age()
    {
        string?[] pinned = ["path:edge", "path:code"];
        WindowRef[] windows =
        [
            new(30, "path:notepad", Sequence: 3),
            new(10, "path:spotify", Sequence: 1),
            new(20, "path:code", Sequence: 2),
        ];

        List<DockPlanEntry> plan = DockPlanner.Plan(pinned, windows);

        Assert.Equal(["path:edge", "path:code", "path:spotify", "path:notepad"], plan.Select(e => e.Key));
        Assert.Equal([0, 1, -1, -1], plan.Select(e => e.PinnedIndex));
        Assert.False(plan[0].IsRunning);
        Assert.True(plan[1].IsRunning);
        Assert.True(plan[1].IsPinned);
        Assert.False(plan[2].IsPinned);
    }

    [Fact]
    public void Windows_of_one_app_share_an_icon_and_are_ordered_oldest_first()
    {
        WindowRef[] windows =
        [
            new(7, "path:explorer", Sequence: 9),
            new(5, "path:explorer", Sequence: 2),
            new(6, "path:explorer", Sequence: 4),
        ];

        List<DockPlanEntry> plan = DockPlanner.Plan([], windows);

        DockPlanEntry entry = Assert.Single(plan);
        Assert.Equal([5L, 6L, 7L], entry.WindowIds);
    }

    [Fact]
    public void Duplicate_and_empty_pins_and_keyless_windows_are_ignored()
    {
        string?[] pinned = ["path:a", null, "", "path:a"];
        WindowRef[] windows = [new(1, "", 1), new(2, "path:a", 2)];

        List<DockPlanEntry> plan = DockPlanner.Plan(pinned, windows);

        DockPlanEntry entry = Assert.Single(plan);
        Assert.Equal("path:a", entry.Key);
        Assert.Equal(0, entry.PinnedIndex);
        Assert.Equal([2L], entry.WindowIds);
    }

    [Fact]
    public void An_unpinned_app_keeps_its_place_while_other_apps_open_and_close()
    {
        WindowRef a = new(1, "path:a", 1);
        WindowRef b = new(2, "path:b", 2);
        WindowRef c = new(3, "path:c", 3);

        Assert.Equal(["path:a", "path:b", "path:c"], DockPlanner.Plan([], [c, a, b]).Select(e => e.Key));
        Assert.Equal(["path:a", "path:c"], DockPlanner.Plan([], [c, a]).Select(e => e.Key));
    }
}

public class CollectionSyncTests
{
    private static void Sync(ObservableCollection<string> target, params string[] desired)
    {
        CollectionSync.Apply(target, desired, item => item, key => key);
    }

    [Fact]
    public void Produces_the_desired_order()
    {
        var target = new ObservableCollection<string> { "a", "b", "c", "d" };

        Sync(target, "d", "x", "b", "a");

        Assert.Equal(["d", "x", "b", "a"], target);
    }

    [Fact]
    public void Leaves_an_already_matching_collection_untouched()
    {
        var target = new ObservableCollection<string> { "a", "b", "c" };
        int changes = 0;
        target.CollectionChanged += (_, _) => changes++;

        Sync(target, "a", "b", "c");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Existing_items_are_moved_not_recreated()
    {
        var target = new ObservableCollection<string> { "a", "b", "c" };
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);
        int created = 0;

        CollectionSync.Apply(target, ["c", "a", "b"], item => item, key => { created++; return key; });

        Assert.Equal(["c", "a", "b"], target);
        Assert.Equal(0, created);
        Assert.All(actions, action => Assert.Equal(NotifyCollectionChangedAction.Move, action));
    }

    [Fact]
    public void Reports_removed_items()
    {
        var target = new ObservableCollection<string> { "a", "b", "c" };
        var removed = new List<string>();

        CollectionSync.Apply(target, ["b"], item => item, key => key, removed.Add);

        Assert.Equal(["b"], target);
        Assert.Equal(["c", "a"], removed);
    }

    [Fact]
    public void Fills_an_empty_collection()
    {
        var target = new ObservableCollection<string>();

        Sync(target, "a", "b");

        Assert.Equal(["a", "b"], target);
    }
}

public class DockClickTests
{
    private static long NoStamp(long id) => 0;

    [Fact]
    public void App_without_windows_is_launched()
    {
        Assert.Equal(new DockClickResult(DockClickAction.Launch, 0), DockClick.Decide([], 0, NoStamp));
    }

    [Fact]
    public void Lone_foreground_window_is_minimised()
    {
        Assert.Equal(new DockClickResult(DockClickAction.Minimize, 42), DockClick.Decide([42], 42, NoStamp));
    }

    [Fact]
    public void Background_app_brings_its_most_recently_used_window_forward()
    {
        var stamps = new Dictionary<long, long> { [1] = 5, [2] = 9, [3] = 7 };

        DockClickResult result = DockClick.Decide([1, 2, 3], activeWindowId: 99, id => stamps[id]);

        Assert.Equal(new DockClickResult(DockClickAction.Activate, 2), result);
    }

    [Fact]
    public void Background_app_with_no_history_uses_its_oldest_window()
    {
        Assert.Equal(new DockClickResult(DockClickAction.Activate, 1), DockClick.Decide([1, 2, 3], 0, NoStamp));
    }

    [Fact]
    public void Clicking_a_foreground_app_with_several_windows_cycles_through_them()
    {
        Assert.Equal(new DockClickResult(DockClickAction.Activate, 2), DockClick.Decide([1, 2, 3], 1, NoStamp));
        Assert.Equal(new DockClickResult(DockClickAction.Activate, 3), DockClick.Decide([1, 2, 3], 2, NoStamp));
        Assert.Equal(new DockClickResult(DockClickAction.Activate, 1), DockClick.Decide([1, 2, 3], 3, NoStamp));
    }
}
