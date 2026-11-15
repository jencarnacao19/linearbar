using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinearBar.Core.Plugins;

/// <summary>
/// What a plugin wants the bar to show. Script plugins print it as JSON; built-in plugins
/// build it in code. Everything is optional: a plugin that prints just a line of text is a
/// valid plugin.
/// </summary>
public sealed record PluginOutput
{
    /// <summary>Short text in the bar, e.g. "18°" or "42 %".</summary>
    public string? Text { get; init; }

    /// <summary>Icon name (see <c>PluginIcons</c>), a single emoji/character, or a path to an image file.</summary>
    public string? Icon { get; init; }

    /// <summary>Accent colour as "#RRGGBB", used for the icon and bars.</summary>
    public string? Color { get; init; }

    /// <summary>Tooltip text; may contain line breaks.</summary>
    public string? Tooltip { get; init; }

    /// <summary>Small gauges under the text, e.g. the 5-hour and weekly limits.</summary>
    public IReadOnlyList<PluginBar> Bars { get; init; } = [];

    /// <summary>Values for a small line graph next to the text.</summary>
    public IReadOnlyList<double> Graph { get; init; } = [];

    /// <summary>Hide the widget (e.g. nothing is playing).</summary>
    public bool Hidden { get; init; }

    /// <summary>Content of the pop-up panel opened by clicking the widget.</summary>
    public PluginPanel? Panel { get; init; }

    /// <summary>Entries of the right-click menu (and of the panel's buttons when it has none).</summary>
    public IReadOnlyList<PluginAction> Menu { get; init; } = [];

    /// <summary>What a left click does: "panel" (default when there is one), "menu", or an action.</summary>
    public PluginAction? Click { get; init; }

    /// <summary>Set by the host when the plugin failed; shown instead of normal content.</summary>
    [JsonIgnore]
    public string? Error { get; init; }

    public static PluginOutput Failure(string message) => new() { Error = message, Icon = "warning", Tooltip = message };
}

/// <summary>A labelled gauge from 0 to 1.</summary>
public sealed record PluginBar
{
    public string? Label { get; init; }

    /// <summary>Fill from 0 to 1.</summary>
    public double Value { get; init; }

    /// <summary>Optional colour as "#RRGGBB"; defaults to the plugin colour, turning amber/red when nearly full.</summary>
    public string? Color { get; init; }

    /// <summary>Optional text on the right, e.g. "reinicia 14:00".</summary>
    public string? Detail { get; init; }
}

/// <summary>The pop-up panel of a plugin.</summary>
public sealed record PluginPanel
{
    public string? Title { get; init; }

    public string? Subtitle { get; init; }

    /// <summary>Large image at the top, e.g. album art (path or URL).</summary>
    public string? Image { get; init; }

    public IReadOnlyList<PluginRow> Rows { get; init; } = [];

    /// <summary>Buttons at the bottom of the panel.</summary>
    public IReadOnlyList<PluginAction> Buttons { get; init; } = [];
}

/// <summary>A row in a plugin panel: label, value, and an optional gauge.</summary>
public sealed record PluginRow
{
    public string? Icon { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    public string? Value { get; init; }

    /// <summary>Optional gauge from 0 to 1.</summary>
    public double? Progress { get; init; }

    public string? Color { get; init; }
}

/// <summary>Something a plugin lets the user do.</summary>
public sealed record PluginAction
{
    public string? Title { get; init; }

    public string? Icon { get; init; }

    /// <summary>URL, file or app to open.</summary>
    public string? Open { get; init; }

    /// <summary>Command line to run (script plugins), relative to the plugin folder.</summary>
    public string? Run { get; init; }

    /// <summary>
    /// Built-in action: "refresh", "panel", "menu", or an action the plugin itself handles
    /// (e.g. "media.next" for the now-playing plugin).
    /// </summary>
    public string? Action { get; init; }

    /// <summary>Copy this text to the clipboard.</summary>
    public string? Copy { get; init; }
}

/// <summary>Reads plugin output, being forgiving about what scripts print.</summary>
public static class PluginOutputParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientListConverterFactory() },
    };

    /// <summary>
    /// Parses what a plugin printed: a JSON object, or — for the simplest scripts — plain
    /// text, whose first line becomes the widget text and the rest the tooltip.
    /// </summary>
    public static PluginOutput Parse(string? output)
    {
        string text = (output ?? string.Empty).Trim().TrimStart('﻿');
        if (text.Length == 0)
        {
            return new PluginOutput { Hidden = true };
        }

        if (text.StartsWith('{'))
        {
            try
            {
                PluginOutput? parsed = JsonSerializer.Deserialize<PluginOutput>(text, Options);
                return Normalize(parsed ?? new PluginOutput());
            }
            catch (JsonException ex)
            {
                return PluginOutput.Failure("Saída JSON inválida: " + ex.Message);
            }
        }

        string[] lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new PluginOutput
        {
            Text = lines[0],
            Tooltip = lines.Length > 1 ? string.Join('\n', lines[1..]) : null,
        };
    }

    public static string Serialize(PluginOutput output) => JsonSerializer.Serialize(output, Options);

    private static PluginOutput Normalize(PluginOutput output)
    {
        // Missing arrays come back as null from the serializer; clamp gauges into range.
        var bars = new List<PluginBar>();
        foreach (PluginBar? bar in output.Bars ?? [])
        {
            if (bar is not null)
            {
                bars.Add(bar with { Value = Clamp01(bar.Value) });
            }
        }

        PluginPanel? panel = output.Panel;
        if (panel is not null)
        {
            var rows = new List<PluginRow>();
            foreach (PluginRow? row in panel.Rows ?? [])
            {
                if (row is not null)
                {
                    rows.Add(row.Progress is { } p ? row with { Progress = Clamp01(p) } : row);
                }
            }

            panel = panel with { Rows = rows, Buttons = Compact(panel.Buttons) };
        }

        var graph = new List<double>();
        foreach (double value in output.Graph ?? [])
        {
            graph.Add(double.IsFinite(value) ? value : 0);
        }

        return output with { Bars = bars, Graph = graph, Panel = panel, Menu = Compact(output.Menu) };
    }

    private static IReadOnlyList<PluginAction> Compact(IReadOnlyList<PluginAction>? actions)
    {
        var list = new List<PluginAction>();
        foreach (PluginAction? action in actions ?? [])
        {
            if (action is not null)
            {
                list.Add(action);
            }
        }

        return list;
    }

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

/// <summary>
/// Reads <see cref="IReadOnlyList{T}"/> properties leniently, because scripting languages
/// rarely print lists the way a strict schema expects: Windows PowerShell 5.1 turns some
/// arrays into <c>{"value": [...], "Count": n}</c> and single-element arrays into the bare
/// element. All of those are accepted.
/// </summary>
internal sealed class LenientListConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class LenientListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return ReadElement(document.RootElement, options);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (T item in value)
            {
                JsonSerializer.Serialize(writer, item, options);
            }

            writer.WriteEndArray();
        }

        private static List<T> ReadElement(JsonElement element, JsonSerializerOptions options)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    return [];

                case JsonValueKind.Array:
                    var list = new List<T>(element.GetArrayLength());
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        T? value = item.Deserialize<T>(options);
                        if (value is not null)
                        {
                            list.Add(value);
                        }
                    }

                    return list;

                case JsonValueKind.Object when TryGetValueProperty(element, out JsonElement wrapped):
                    return ReadElement(wrapped, options);

                default:
                    T? single = element.Deserialize<T>(options);
                    return single is null ? [] : [single];
            }
        }

        private static bool TryGetValueProperty(JsonElement element, out JsonElement wrapped)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "value", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                {
                    wrapped = property.Value;
                    return true;
                }
            }

            wrapped = default;
            return false;
        }
    }
}
