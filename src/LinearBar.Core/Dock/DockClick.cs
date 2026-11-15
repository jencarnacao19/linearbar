namespace LinearBar.Core.Dock;

public enum DockClickAction
{
    /// <summary>The app has no windows: start it.</summary>
    Launch,

    /// <summary>Bring a window of the app to the front.</summary>
    Activate,

    /// <summary>The app's only window is already in front: minimise it.</summary>
    Minimize,
}

/// <summary>What a click on a dock icon should do.</summary>
/// <param name="Action">The action to take.</param>
/// <param name="WindowId">Window the action applies to; 0 for <see cref="DockClickAction.Launch"/>.</param>
public readonly record struct DockClickResult(DockClickAction Action, long WindowId);

/// <summary>
/// Click behaviour of a dock icon, kept free of any Win32 so it can be tested:
/// launch when not running, bring the most recently used window forward when in the
/// background, minimise a lone foreground window, and cycle through windows when the app
/// has several and one of them is already in front.
/// </summary>
public static class DockClick
{
    /// <param name="windowIds">Windows of the app, oldest first.</param>
    /// <param name="activeWindowId">
    /// The window that was in the foreground when the mouse button went down, or 0. It must
    /// be captured on press: by the time the click is delivered the bar itself has focus.
    /// </param>
    /// <param name="activationStamp">
    /// Returns when a window was last activated (larger is more recent, 0 for never).
    /// </param>
    public static DockClickResult Decide(IReadOnlyList<long> windowIds, long activeWindowId, Func<long, long> activationStamp)
    {
        if (windowIds.Count == 0)
        {
            return new DockClickResult(DockClickAction.Launch, 0);
        }

        int activeIndex = -1;
        if (activeWindowId != 0)
        {
            for (int i = 0; i < windowIds.Count; i++)
            {
                if (windowIds[i] == activeWindowId)
                {
                    activeIndex = i;
                    break;
                }
            }
        }

        if (activeIndex >= 0)
        {
            return windowIds.Count == 1
                ? new DockClickResult(DockClickAction.Minimize, activeWindowId)
                : new DockClickResult(DockClickAction.Activate, windowIds[(activeIndex + 1) % windowIds.Count]);
        }

        long best = windowIds[0];
        long bestStamp = activationStamp(best);
        for (int i = 1; i < windowIds.Count; i++)
        {
            long stamp = activationStamp(windowIds[i]);
            if (stamp > bestStamp)
            {
                best = windowIds[i];
                bestStamp = stamp;
            }
        }

        return new DockClickResult(DockClickAction.Activate, best);
    }
}
