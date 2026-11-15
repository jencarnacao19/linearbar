using System.Text.Json;

namespace LinearBar.Core.Usage;

/// <summary>One assistant response recorded in a Claude Code session log.</summary>
public readonly record struct TokenEntry(DateTimeOffset Time, string Model, long Input, long Output, long CacheCreation, long CacheRead)
{
    public long Total => Input + Output + CacheCreation + CacheRead;
}

/// <summary>Usage summary shown by the Claude widget.</summary>
public sealed record ClaudeUsageSummary
{
    /// <summary>Start of the current 5-hour block, or null when there is no active block.</summary>
    public DateTimeOffset? BlockStart { get; init; }

    /// <summary>When the current block ends (Claude's usage window resets).</summary>
    public DateTimeOffset? BlockEnd => BlockStart?.Add(ClaudeUsage.BlockLength);

    public long BlockTokens { get; init; }

    public long BlockOutputTokens { get; init; }

    public int BlockMessages { get; init; }

    public long TodayTokens { get; init; }

    public long WeekTokens { get; init; }

    /// <summary>Tokens per model in the current block, largest first.</summary>
    public IReadOnlyList<(string Model, long Tokens)> BlockModels { get; init; } = [];

    /// <summary>Tokens per hour over the last 12 hours, oldest first (for the graph).</summary>
    public IReadOnlyList<double> Hourly { get; init; } = [];

    public DateTimeOffset? LastActivity { get; init; }
}

/// <summary>
/// Reads Claude Code's local session logs (<c>~/.claude/projects/**/*.jsonl</c>, or
/// <c>~/.config/claude/projects</c>, or <c>CLAUDE_CONFIG_DIR</c>) and sums token usage the way
/// tools such as ccusage do: per message, de-duplicated, grouped in 5-hour blocks that start
/// on the hour of their first message.
/// </summary>
/// <remarks>
/// Local logs are not Anthropic's billing records: the plan's actual limits are applied
/// server-side and are not in the logs, so this reports tokens, not a percentage, unless the
/// user configures a token budget.
/// </remarks>
public sealed class ClaudeUsage
{
    public static readonly TimeSpan BlockLength = TimeSpan.FromHours(5);

    private readonly JsonlTail _tail = new();
    private readonly Dictionary<string, TokenEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Directories that hold Claude Code's <c>projects</c> logs on this machine.</summary>
    public static List<string> DefaultRoots(Func<string, string?> environment, string home)
    {
        var roots = new List<string>();
        string? configured = environment("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (string dir in configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                roots.Add(Path.Combine(dir, "projects"));
            }

            return roots;
        }

        roots.Add(Path.Combine(home, ".config", "claude", "projects"));
        roots.Add(Path.Combine(home, ".claude", "projects"));
        return roots;
    }

    /// <summary>Reads what was appended to the logs and returns the up-to-date summary.</summary>
    public ClaudeUsageSummary Refresh(IEnumerable<string> roots, DateTimeOffset now)
    {
        DateTime since = now.UtcDateTime - TimeSpan.FromDays(8);
        foreach ((_, string line) in _tail.ReadNew(roots, since))
        {
            Add(line);
        }

        // Keep memory bounded: nothing older than a week matters.
        DateTimeOffset cutoff = now - TimeSpan.FromDays(8);
        foreach (string key in _entries.Where(e => e.Value.Time < cutoff).Select(e => e.Key).ToList())
        {
            _entries.Remove(key);
        }

        return Summarize(_entries.Values, now);
    }

    /// <summary>Parses one log line; returns false when it carries no usage.</summary>
    public bool Add(string line)
    {
        if (!line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out JsonElement timestamp)
                || !DateTimeOffset.TryParse(timestamp.GetString(), out DateTimeOffset time))
            {
                return false;
            }

            string id = (message.TryGetProperty("id", out JsonElement mid) ? mid.GetString() : null) ?? string.Empty;
            string request = (root.TryGetProperty("requestId", out JsonElement rid) ? rid.GetString() : null) ?? string.Empty;
            string key = id.Length + request.Length > 0 ? id + "|" + request : time.ToString("O") + line.Length;

            string model = message.TryGetProperty("model", out JsonElement m) ? m.GetString() ?? "?" : "?";
            if (model == "<synthetic>")
            {
                return false;
            }

            var entry = new TokenEntry(
                time,
                model,
                Number(usage, "input_tokens"),
                Number(usage, "output_tokens"),
                Number(usage, "cache_creation_input_tokens"),
                Number(usage, "cache_read_input_tokens"));

            if (entry.Total <= 0)
            {
                return false;
            }

            // Streaming writes the same message more than once; keep the most complete copy.
            if (!_entries.TryGetValue(key, out TokenEntry existing) || entry.Total >= existing.Total)
            {
                _entries[key] = entry;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Groups entries into 5-hour blocks and summarises the current state.</summary>
    public static ClaudeUsageSummary Summarize(IEnumerable<TokenEntry> entries, DateTimeOffset now)
    {
        List<TokenEntry> sorted = entries.OrderBy(e => e.Time).ToList();
        if (sorted.Count == 0)
        {
            return new ClaudeUsageSummary { Hourly = new double[12] };
        }

        // Blocks: start on the hour of the first message; a message after the block ended
        // (or after a 5-hour gap) starts a new one.
        DateTimeOffset? blockStart = null;
        DateTimeOffset last = sorted[0].Time;
        foreach (TokenEntry entry in sorted)
        {
            if (blockStart is null || entry.Time >= blockStart.Value + BlockLength || entry.Time - last >= BlockLength)
            {
                blockStart = FloorToHour(entry.Time);
            }

            last = entry.Time;
        }

        bool active = blockStart is { } start && now < start + BlockLength && now - last < BlockLength;
        DateTimeOffset localNow = now.ToLocalTime();
        DateTimeOffset todayStart = new DateTimeOffset(localNow.Date, localNow.Offset);

        long blockTokens = 0, blockOutput = 0, today = 0, week = 0;
        int blockMessages = 0;
        var models = new Dictionary<string, long>(StringComparer.Ordinal);
        var hourly = new double[12];

        foreach (TokenEntry entry in sorted)
        {
            if (entry.Time > now)
            {
                continue;
            }

            if (now - entry.Time <= TimeSpan.FromDays(7))
            {
                week += entry.Total;
            }

            if (entry.Time >= todayStart)
            {
                today += entry.Total;
            }

            int hoursAgo = (int)Math.Floor((now - entry.Time).TotalHours);
            if (hoursAgo is >= 0 and < 12)
            {
                hourly[11 - hoursAgo] += entry.Total;
            }

            if (active && entry.Time >= blockStart!.Value)
            {
                blockTokens += entry.Total;
                blockOutput += entry.Output;
                blockMessages++;
                models[ShortModel(entry.Model)] = models.GetValueOrDefault(ShortModel(entry.Model)) + entry.Total;
            }
        }

        return new ClaudeUsageSummary
        {
            BlockStart = active ? blockStart : null,
            BlockTokens = blockTokens,
            BlockOutputTokens = blockOutput,
            BlockMessages = blockMessages,
            TodayTokens = today,
            WeekTokens = week,
            BlockModels = models.OrderByDescending(p => p.Value).Select(p => (p.Key, p.Value)).ToList(),
            Hourly = hourly,
            LastActivity = last,
        };
    }

    /// <summary>"claude-opus-4-5-20251101" → "opus-4-5".</summary>
    public static string ShortModel(string model)
    {
        string name = model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? model[7..] : model;
        int date = name.LastIndexOf('-');
        if (date > 0 && name.Length - date - 1 == 8 && name[(date + 1)..].All(char.IsAsciiDigit))
        {
            name = name[..date];
        }

        return name;
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset time) =>
        new(time.Year, time.Month, time.Day, time.Hour, 0, 0, time.Offset);

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? Math.Max(0, n) : 0;
}
