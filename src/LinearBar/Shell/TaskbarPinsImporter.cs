using System.IO;
using LinearBar.Core.Config;
using LinearBar.Infrastructure;

namespace LinearBar.Shell;

/// <summary>
/// Seeds the dock with the apps the user already pinned to the Windows taskbar, so the
/// first run does not start with an empty dock.
/// </summary>
/// <remarks>
/// Windows keeps classic taskbar pins as shortcuts in a well-known folder. Store apps pinned
/// to the taskbar are not stored there (they live in an undocumented registry blob) and are
/// therefore not imported; the user can pin them from the dock once they are running.
/// </remarks>
internal static class TaskbarPinsImporter
{
    public static string PinsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static List<PinnedApp> Import()
    {
        var pins = new List<PinnedApp>();

        try
        {
            if (!Directory.Exists(PinsFolder))
            {
                return pins;
            }

            // The real order is in the registry blob; creation time is the closest stand-in.
            IOrderedEnumerable<FileInfo> shortcuts = new DirectoryInfo(PinsFolder)
                .EnumerateFiles("*.lnk")
                .OrderBy(file => file.CreationTimeUtc);

            foreach (FileInfo file in shortcuts)
            {
                if (ShortcutResolver.TryResolve(file.FullName) is not { } shortcut)
                {
                    continue;
                }

                // A shortcut without a file target points at a shell location (This PC, a
                // library, …), which opens in File Explorer.
                string target = string.IsNullOrWhiteSpace(shortcut.TargetPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")
                    : shortcut.TargetPath;

                pins.Add(new PinnedApp
                {
                    Name = Path.GetFileNameWithoutExtension(file.Name),
                    Path = file.FullName,
                    Target = target,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not import the Windows taskbar pins: " + ex.Message);
        }

        Log.Info($"Imported {pins.Count} pinned app(s) from the Windows taskbar.");
        return pins;
    }
}
