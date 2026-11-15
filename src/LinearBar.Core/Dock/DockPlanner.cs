namespace LinearBar.Core.Dock;

/// <summary>A top-level window as seen by the dock planner.</summary>
/// <param name="Id">Opaque window identity (the HWND value).</param>
/// <param name="AppKey">Key of the app the window belongs to (see <c>AppKey</c>).</param>
/// <param name="Sequence">Order in which the window was first seen; lower is older.</param>
public readonly record struct WindowRef(long Id, string AppKey, long Sequence);

/// <summary>One icon in the dock.</summary>
/// <param name="Key">App key shared by the pin and its windows.</param>
/// <param name="PinnedIndex">Index into the pinned list, or -1 when the app is running but not pinned.</param>
/// <param name="WindowIds">Windows of the app, oldest first. Empty for a pinned app that is not running.</param>
public sealed record DockPlanEntry(string Key, int PinnedIndex, IReadOnlyList<long> WindowIds)
{
    public bool IsPinned => PinnedIndex >= 0;

    public bool IsRunning => WindowIds.Count > 0;
}

/// <summary>
/// Decides which icons the dock shows and in which order: pinned apps first, in the order
/// the user pinned them, followed by running apps that are not pinned, in the order they
/// were opened. Windows are grouped by app, one icon per app, as in the macOS dock.
/// </summary>
public static class DockPlanner
{
    /// <param name="pinnedKeys">App key of every pinned app, in display order. Null entries are ignored.</param>
    /// <param name="windows">Windows that should appear in the dock.</param>
    public static List<DockPlanEntry> Plan(IReadOnlyList<string?> pinnedKeys, IEnumerable<WindowRef> windows)
    {
        var windowsByApp = new Dictionary<string, List<WindowRef>>(StringComparer.Ordinal);
        foreach (WindowRef window in windows)
        {
            if (string.IsNullOrEmpty(window.AppKey))
            {
                continue;
            }

            if (!windowsByApp.TryGetValue(window.AppKey, out List<WindowRef>? list))
            {
                list = [];
                windowsByApp[window.AppKey] = list;
            }

            list.Add(window);
        }

        foreach (List<WindowRef> list in windowsByApp.Values)
        {
            list.Sort(static (a, b) => a.Sequence != b.Sequence ? a.Sequence.CompareTo(b.Sequence) : a.Id.CompareTo(b.Id));
        }

        var plan = new List<DockPlanEntry>(pinnedKeys.Count + windowsByApp.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < pinnedKeys.Count; i++)
        {
            string? key = pinnedKeys[i];
            if (string.IsNullOrEmpty(key) || !placed.Add(key))
            {
                continue;
            }

            IReadOnlyList<long> ids = windowsByApp.TryGetValue(key, out List<WindowRef>? list)
                ? list.ConvertAll(static w => w.Id)
                : [];
            plan.Add(new DockPlanEntry(key, i, ids));
        }

        var unpinned = new List<KeyValuePair<string, List<WindowRef>>>();
        foreach (KeyValuePair<string, List<WindowRef>> pair in windowsByApp)
        {
            if (!placed.Contains(pair.Key))
            {
                unpinned.Add(pair);
            }
        }

        // Oldest app first, so icons keep their place while their app stays open.
        unpinned.Sort(static (a, b) =>
        {
            int bySequence = a.Value[0].Sequence.CompareTo(b.Value[0].Sequence);
            return bySequence != 0 ? bySequence : string.CompareOrdinal(a.Key, b.Key);
        });

        foreach (KeyValuePair<string, List<WindowRef>> pair in unpinned)
        {
            plan.Add(new DockPlanEntry(pair.Key, -1, pair.Value.ConvertAll(static w => w.Id)));
        }

        return plan;
    }
}
