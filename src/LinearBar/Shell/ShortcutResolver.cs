using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using LinearBar.Infrastructure;

namespace LinearBar.Shell;

/// <summary>Reads the target of Windows shortcut (.lnk) files through the shell's IShellLink.</summary>
internal static class ShortcutResolver
{
    private const int MaxPath = 1024;

    /// <summary>What a shortcut points to.</summary>
    /// <param name="TargetPath">File the shortcut launches; empty for shortcuts to shell locations.</param>
    /// <param name="Arguments">Command-line arguments stored in the shortcut.</param>
    internal sealed record Shortcut(string TargetPath, string Arguments);

    /// <summary>
    /// Resolves <paramref name="shortcutPath"/>. Returns null when the file is not a readable
    /// shortcut. Must be called on an STA thread (the UI thread is fine).
    /// </summary>
    public static Shortcut? TryResolve(string shortcutPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);

            var shellLink = (IShellLinkW)link;
            var path = new StringBuilder(MaxPath);
            var arguments = new StringBuilder(MaxPath);

            shellLink.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            shellLink.GetArguments(arguments, arguments.Capacity);

            return new Shortcut(Environment.ExpandEnvironmentVariables(path.ToString()), arguments.ToString());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cannot read shortcut '{shortcutPath}': {ex.Message}");
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>
    /// Leading part of IShellLinkW. COM interfaces are positional: methods must be declared
    /// in vtable order up to the last one used, even when they are not called.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);

        [PreserveSig]
        int GetIDList(out IntPtr pidl);

        [PreserveSig]
        int SetIDList(IntPtr pidl);

        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        [PreserveSig]
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        [PreserveSig]
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
    }
}
