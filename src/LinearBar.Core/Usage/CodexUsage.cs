using System.Text.Json;

namespace LinearBar.Core.Usage;

/// <summary>One of Codex's rolling rate-limit windows, as reported by the server.</summary>
/// <param name="UsedPercent">0–100.</param>
/// <param name="WindowMinutes">Length of the window (300 = 5 hours, 10080 = 7 days).</param>
/// <param name="ResetsAt">When the window resets, or null when unknown.</param>
public sealed record RateWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt);

/// <summary>Usage summary shown by the Codex widget.</summary>
public sealed record CodexUsageSummary
{
    /// <summary>The short window (normally 5 hours).</summary>
    public RateWindow? Primary { get; init; }

    /// <summary>The long window (normally 7 days).</summary>
    public RateWindow? Secondary { get; init; }

    /// <summary>When Codex last reported its limits.</summary>
    public DateTimeOffset? ReportedAt { get; init; }

    public long TodayTokens { get; init; }

    public long WeekTokens { get; init; }
}

/// <summary>
/// Reads Codex CLI session logs (<c>~/.codex/sessions/**/rollout-*.jsonl</c>, or under
/// <c>CODEX_HOME</c>). Codex writes a <c>token_count</c> event after each turn that includes
/// the rate limits as the server reported them — the same numbers <c>/status</c> shows — so
/// the widget can show real percentages of the 5-hour and weekly limits.
/// </summary>
public sealed class CodexUsage
{
    private readonly JsonlTail _tail = new();
    private readonly List<(DateTimeOffset Time, long Tokens)> _turns = [];
    private (DateTimeOffset Time, RateWindow? Primary, RateWindow? Secondary)? _latest;

    public static string DefaultRoot(Func<string, string?> environment, string home)
    {
        string? configured = environment("CODEX_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(configured) ? Path.Combine(home, ".codex") : configured, "sessions");
    }

    public CodexUsageSummary Refresh(IEnumerable<string> roots, DateTimeOffset now)
    {
        foreach ((_, string line) in _tail.ReadNew(roots, now.UtcDateTime - TimeSpan.FromDays(8)))
        {
            Add(line);
        }

        _turns.RemoveAll(t => now - t.Time > TimeSpan.FromDays(8));
        return Summarize(now);
    }

    /// <summary>Parses one log line; returns false when it is not a token-count event.</summary>
    public bool Add(string line)
    {
        if (!line.Contains("token_count", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("type", out JsonElement type) || type.GetString() != "token_count")
            {
                return false;
            }

            DateTimeOffset time = root.TryGetProperty("timestamp", out JsonElement ts) && DateTimeOffset.TryParse(ts.GetString(), out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            if (payload.TryGetProperty("info", out JsonElement info) && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("last_token_usage", out JsonElement lastUsage) && lastUsage.ValueKind == JsonValueKind.Object)
            {
                long tokens = Number(lastUsage, "total_tokens");
                if (tokens <= 0)
                {
                    tokens = Number(lastUsage, "input_tokens") + Number(lastUsage, "output_tokens");
                }

                if (tokens > 0 && time != DateTimeOffset.MinValue)
                {
                    _turns.Add((time, tokens));
                }
            }

            // Rate limits sit on the payload (current versions) or inside "info" (older ones).
            JsonElement limits = default;
            bool hasLimits = payload.TryGetProperty("rate_limits", out limits) && limits.ValueKind == JsonValueKind.Object;
            if (!hasLimits && info.ValueKind == JsonValueKind.Object)
            {
                hasLimits = info.TryGetProperty("rate_limits", out limits) && limits.ValueKind == JsonValueKind.Object;
            }

            if (hasLimits && (_latest is null || time >= _latest.Value.Time))
            {
                _latest = (time, Window(limits, "primary", time), Window(limits, "secondary", time));
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public CodexUsageSummary Summarize(DateTimeOffset now)
    {
        DateTimeOffset localNow = now.ToLocalTime();
        var todayStart = new DateTimeOffset(localNow.Date, localNow.Offset);

        long today = 0, week = 0;
        foreach ((DateTimeOffset time, long tokens) in _turns)
        {
            if (time > now)
            {
                continue;
            }

            if (now - time <= TimeSpan.FromDays(7))
            {
                week += tokens;
            }

            if (time >= todayStart)
            {
                today += tokens;
            }
        }

        // A window that has reset since Codex last reported is back at 0 %.
        RateWindow? Current(RateWindow? window) =>
            window is { ResetsAt: { } reset } && reset <= now ? window with { UsedPercent = 0, ResetsAt = null } : window;

        return new CodexUsageSummary
        {
            Primary = Current(_latest?.Primary),
            Secondary = Current(_latest?.Secondary),
            ReportedAt = _latest?.Time,
            TodayTokens = today,
            WeekTokens = week,
        };
    }

    private static RateWindow? Window(JsonElement limits, string name, DateTimeOffset reportedAt)
    {
        if (!limits.TryGetProperty(name, out JsonElement window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        double used = window.TryGetProperty("used_percent", out JsonElement u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : 0;
        int minutes = window.TryGetProperty("window_minutes", out JsonElement w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : 0;

        DateTimeOffset? resets = null;
        if (window.TryGetProperty("resets_at", out JsonElement at) && at.ValueKind == JsonValueKind.Number)
        {
            resets = DateTimeOffset.FromUnixTimeSeconds(at.GetInt64());
        }
        else if (window.TryGetProperty("resets_in_seconds", out JsonElement inSeconds) && inSeconds.ValueKind == JsonValueKind.Number
                 && reportedAt != DateTimeOffset.MinValue)
        {
            resets = reportedAt.AddSeconds(inSeconds.GetDouble());
        }

        return new RateWindow(Math.Clamp(used, 0, 100), minutes, resets);
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? Math.Max(0, n) : 0;
}
