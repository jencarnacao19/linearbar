namespace LinearBar.Core.Config;

/// <summary>
/// Stable identity of an application in the dock. Windows that share a key are grouped under
/// one icon, and a pinned app is "running" when a window with its key exists.
/// </summary>
public static class AppKey
{
    private const string PathPrefix = "path:";
    private const string AumidPrefix = "aumid:";

    /// <summary>Key for a classic desktop app, identified by its executable path.</summary>
    public static string ForPath(string path) => PathPrefix + NormalizePath(path);

    /// <summary>Key for a Store/UWP app, identified by its Application User Model ID.</summary>
    public static string ForAumid(string aumid) => AumidPrefix + aumid.Trim().ToLowerInvariant();

    /// <summary>Key for a pinned app, or null when the pin identifies nothing.</summary>
    public static string? ForPinned(PinnedApp pin)
    {
        if (!string.IsNullOrWhiteSpace(pin.Aumid))
        {
            return ForAumid(pin.Aumid);
        }

        string? path = !string.IsNullOrWhiteSpace(pin.Target) ? pin.Target : pin.Path;
        return string.IsNullOrWhiteSpace(path) ? null : ForPath(path);
    }

    /// <summary>
    /// Key for a window, given what is known about it. Store apps are hosted by a shared
    /// frame process, so their executable path is meaningless and the AUMID is used instead.
    /// </summary>
    public static string? ForWindow(string? executablePath, string? aumid, bool isStoreApp)
    {
        if (isStoreApp && !string.IsNullOrWhiteSpace(aumid))
        {
            return ForAumid(aumid);
        }

        return string.IsNullOrWhiteSpace(executablePath) ? null : ForPath(executablePath);
    }

    /// <summary>True when the key was produced by <see cref="ForAumid"/>.</summary>
    public static bool IsAumid(string key) => key.StartsWith(AumidPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Windows paths are case-insensitive and may be written with either slash, quoted, or
    /// with environment variables; fold all of that so equal files compare equal.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        string value = path.Trim().Trim('"');
        value = Environment.ExpandEnvironmentVariables(value);
        value = value.Replace('/', '\\');

        while (value.Length > 3 && value.EndsWith('\\'))
        {
            value = value[..^1];
        }

        return value.ToLowerInvariant();
    }
}
