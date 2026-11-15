using System.Globalization;
using System.IO;
using LinearBar.Infrastructure;
using ManagedShell.AppBar;
using static ManagedShell.Interop.NativeMethods;

namespace LinearBar.Shell;

/// <summary>
/// Hides the Windows (Explorer) taskbar while LinearBar runs and makes sure it comes back.
/// </summary>
/// <remarks>
/// <para>
/// Hiding means switching the Explorer taskbar to auto-hide and hiding its windows;
/// ManagedShell's <see cref="ExplorerHelper"/> does both and undoes them on restore.
/// </para>
/// <para>
/// The danger is a hard kill (Task Manager, power loss): nothing gets to run, and the user
/// is left without any taskbar, with auto-hide switched on. To recover, the original state
/// is written to a marker file before hiding and removed on a clean restore. A marker found
/// at start-up means the previous session died, and the taskbar is put back first.
/// </para>
/// </remarks>
internal sealed class TaskbarGuard(ExplorerHelper explorer)
{
    public static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinearBar",
        "taskbar-hidden.state");

    /// <summary>True while this instance keeps the Windows taskbar hidden.</summary>
    public bool IsHidden { get; private set; }

    /// <summary>Undoes what a previous, uncleanly terminated session left behind.</summary>
    public void RecoverFromUncleanExit()
    {
        if (TryReadMarker() is not { } state)
        {
            return;
        }

        Log.Warn("The previous session did not exit cleanly; restoring the Windows taskbar first.");
        Show(explorer, state);
        DeleteMarker();
    }

    public void Hide()
    {
        if (IsHidden)
        {
            return;
        }

        try
        {
            ABState state = explorer.GetTaskbarState();
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, ((int)state).ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not write the taskbar recovery marker: " + ex.Message);
        }

        explorer.HideExplorerTaskbar = true;
        IsHidden = true;
        Log.Info("Windows taskbar hidden.");
    }

    public void Restore()
    {
        if (!IsHidden)
        {
            return;
        }

        explorer.HideExplorerTaskbar = false;
        IsHidden = false;
        DeleteMarker();
        Log.Info("Windows taskbar restored.");
    }

    /// <summary>
    /// Brings the Windows taskbar back without a running LinearBar. Backs the
    /// <c>--restore-taskbar</c> command-line switch.
    /// </summary>
    public static void ForceRestore()
    {
        ABState state = TryReadMarker() ?? ABState.Default;
        Show(new ExplorerHelper(), state);
        DeleteMarker();
        Log.Info($"Windows taskbar restored on request (state {state}).");
    }

    private static void Show(ExplorerHelper explorer, ABState state)
    {
        explorer.SetTaskbarState(state);
        explorer.SetTaskbarVisibility((int)SetWindowPosFlags.SWP_SHOWWINDOW);
    }

    private static ABState? TryReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath))
            {
                return null;
            }

            string text = File.ReadAllText(MarkerPath).Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && Enum.IsDefined(typeof(ABState), value)
                ? (ABState)value
                : ABState.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ABState.Default;
        }
    }

    private static void DeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not remove the taskbar recovery marker: " + ex.Message);
        }
    }
}
