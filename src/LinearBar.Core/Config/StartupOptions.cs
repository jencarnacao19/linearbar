namespace LinearBar.Core.Config;

/// <summary>Command-line options of the LinearBar executable.</summary>
public sealed record StartupOptions
{
    /// <summary><c>--restore-taskbar</c>: bring the Windows taskbar back and exit. Recovery tool.</summary>
    public bool RestoreTaskbar { get; init; }

    /// <summary><c>--keep-taskbar</c>: do not hide the Windows taskbar for this run.</summary>
    public bool KeepTaskbar { get; init; }

    /// <summary><c>--smoke-test</c>: start, exercise the bar, write a report to the log and exit.</summary>
    public bool SmokeTest { get; init; }

    /// <summary><c>--screenshot &lt;dir&gt;</c>: directory the smoke test saves its screenshots to.</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary><c>--settings &lt;file&gt;</c>: use this settings file instead of the default one.</summary>
    public string? SettingsPath { get; init; }

    /// <summary><c>--log &lt;file&gt;</c>: write the log to this file instead of the default one.</summary>
    public string? LogPath { get; init; }

    /// <summary><c>--verbose</c>: include debug messages in the log.</summary>
    public bool Verbose { get; init; }

    /// <summary>Arguments that were not understood.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var options = new StartupOptions();
        var unknown = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--restore-taskbar":
                    options = options with { RestoreTaskbar = true };
                    break;
                case "--keep-taskbar":
                    options = options with { KeepTaskbar = true };
                    break;
                case "--smoke-test":
                    options = options with { SmokeTest = true };
                    break;
                case "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "--screenshot" when TryTakeValue(args, ref i, out string? value):
                    options = options with { ScreenshotDirectory = value };
                    break;
                case "--settings" when TryTakeValue(args, ref i, out string? value):
                    options = options with { SettingsPath = value };
                    break;
                case "--log" when TryTakeValue(args, ref i, out string? value):
                    options = options with { LogPath = value };
                    break;
                default:
                    unknown.Add(arg);
                    break;
            }
        }

        return options with { Unknown = unknown };
    }

    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        value = null;
        return false;
    }
}
