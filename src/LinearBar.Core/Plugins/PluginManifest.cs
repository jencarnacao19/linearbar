using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinearBar.Core.Plugins;

/// <summary>How the host talks to a script plugin.</summary>
public enum PluginMode
{
    /// <summary>Run the command every <see cref="PluginManifest.IntervalSeconds"/>, read its output.</summary>
    Interval,

    /// <summary>Keep the command running; every line it prints is a new output.</summary>
    Stream,
}

/// <summary>Which island a widget goes in.</summary>
public enum PluginPlacement
{
    Right,
    Left,
}

/// <summary>The <c>plugin.json</c> of a script plugin.</summary>
public sealed record PluginManifest
{
    /// <summary>Unique id: lower-case letters, digits, '-' and '.'.</summary>
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Description { get; init; }

    /// <summary>Command line, run in the plugin folder, e.g. <c>powershell -File meteo.ps1</c>.</summary>
    public string Run { get; init; } = string.Empty;

    public PluginMode Mode { get; init; } = PluginMode.Interval;

    public int IntervalSeconds { get; init; } = 60;

    /// <summary>Seconds an interval run may take before it is killed.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    public PluginPlacement Placement { get; init; } = PluginPlacement.Right;

    /// <summary>Whether the plugin runs when the settings do not mention it.</summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>Declared capabilities ("network", "files", "processes"); shown to the user.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Default options, overridable in the bar settings; passed as environment variables.</summary>
    public Dictionary<string, JsonElement> Options { get; init; } = [];

    /// <summary>Folder the manifest was loaded from. Set by the loader.</summary>
    [JsonIgnore]
    public string Directory { get; init; } = string.Empty;
}

/// <summary>Reads and validates plugin manifests.</summary>
public static class PluginManifestReader
{
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Parses a manifest; returns null and the reason when it is unusable.</summary>
    public static PluginManifest? Parse(string json, string directory, out string? error)
    {
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            error = "plugin.json inválido: " + ex.Message;
            return null;
        }

        if (manifest is null)
        {
            error = "plugin.json vazio";
            return null;
        }

        if (!IsValidId(manifest.Id))
        {
            error = $"id inválido '{manifest.Id}' (use minúsculas, algarismos, '-' e '.')";
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Run))
        {
            error = "falta o campo 'run'";
            return null;
        }

        error = null;
        return manifest with
        {
            Directory = directory,
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
            IntervalSeconds = Math.Clamp(manifest.IntervalSeconds, 1, 24 * 3600),
            TimeoutSeconds = Math.Clamp(manifest.TimeoutSeconds, 1, 600),
            Permissions = manifest.Permissions ?? [],
            Options = manifest.Options ?? [],
        };
    }

    /// <summary>Loads every <c>*/plugin.json</c> under <paramref name="root"/>.</summary>
    public static List<(PluginManifest? Manifest, string Directory, string? Error)> LoadAll(string root)
    {
        var result = new List<(PluginManifest?, string, string?)>();
        if (!System.IO.Directory.Exists(root))
        {
            return result;
        }

        foreach (string directory in System.IO.Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                PluginManifest? manifest = Parse(File.ReadAllText(path), directory, out string? error);
                result.Add((manifest, directory, error));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Add((null, directory, ex.Message));
            }
        }

        return result;
    }

    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= 64
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
        && char.IsAsciiLetterOrDigit(id[0]);
}

/// <summary>Splits command lines the way Windows does (quotes group, backslashes are literal).</summary>
public static class CommandLine
{
    public static List<string> Split(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    /// <summary>Environment-variable name for a plugin option: <c>LINEARBAR_OPT_CITY</c>.</summary>
    public static string OptionVariable(string option)
    {
        var name = new System.Text.StringBuilder("LINEARBAR_OPT_");
        foreach (char c in option)
        {
            name.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return name.ToString();
    }
}
