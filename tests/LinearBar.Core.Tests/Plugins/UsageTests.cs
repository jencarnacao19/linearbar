using System.Globalization;
using LinearBar.Core.Usage;
using LinearBar.Core.Weather;

namespace LinearBar.Core.Tests.Plugins;

public class ClaudeUsageTests
{
    private static string Line(string time, string id, string request, int input, int output, int cacheRead = 0, string model = "claude-opus-4-5-20251101") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + time + "\",\"requestId\":\"" + request + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":" + cacheRead + "}}}";

    [Fact]
    public void Lines_without_usage_or_malformed_are_ignored()
    {
        var usage = new ClaudeUsage();

        Assert.False(usage.Add("""{"type":"user","message":{"content":"hi"}}"""));
        Assert.False(usage.Add("""{ "usage": broken"""));
        Assert.False(usage.Add(Line("2026-11-15T10:00:00Z", "m", "r", 0, 0)));
        Assert.True(usage.Add(Line("2026-11-15T10:00:00Z", "m", "r", 10, 5)));
    }

    [Fact]
    public void Blocks_start_on_the_hour_and_messages_are_deduplicated()
    {
        var usage = new ClaudeUsage();
        usage.Add(Line("2026-11-15T09:20:00Z", "a", "1", 100, 50));
        usage.Add(Line("2026-11-15T09:20:01Z", "a", "1", 100, 60)); // same message streamed again, more complete
        usage.Add(Line("2026-11-15T11:45:00Z", "b", "2", 1000, 500, cacheRead: 2000));
        // An older block that has ended.
        usage.Add(Line("2026-11-15T01:00:00Z", "c", "3", 7, 7));

        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        ClaudeUsageSummary summary = usage.Refresh([], now);

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero), summary.BlockStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 14, 0, 0, TimeSpan.Zero), summary.BlockEnd);
        Assert.Equal(160 + 3500, summary.BlockTokens);
        Assert.Equal(2, summary.BlockMessages);
        Assert.Equal(560, summary.BlockOutputTokens);
        Assert.Equal("opus-4-5", Assert.Single(summary.BlockModels).Model);
        Assert.Equal(12, summary.Hourly.Count);
        Assert.Equal(3500, summary.Hourly[11]);
    }

    [Fact]
    public void No_active_block_after_five_quiet_hours()
    {
        var usage = new ClaudeUsage();
        usage.Add(Line("2026-11-15T03:10:00Z", "a", "1", 100, 50));

        ClaudeUsageSummary summary = usage.Refresh([], new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(summary.BlockStart);
        Assert.Equal(0, summary.BlockTokens);
        Assert.Equal(150, summary.WeekTokens);
    }

    [Fact]
    public void Logs_are_read_incrementally_from_disk()
    {
        string root = Path.Combine(Path.GetTempPath(), "lb-claude-" + Guid.NewGuid().ToString("N"));
        try
        {
            string project = Path.Combine(root, "proj");
            Directory.CreateDirectory(project);
            string file = Path.Combine(project, "s.jsonl");
            var now = DateTimeOffset.UtcNow;
            string t = now.AddMinutes(-10).ToString("O");

            File.WriteAllText(file, Line(t, "a", "1", 10, 10) + "\n" + Line(t, "b", "2", 5, 5)); // last line unterminated
            var usage = new ClaudeUsage();
            Assert.Equal(20, usage.Refresh([root], now).BlockTokens);

            File.AppendAllText(file, "\n" + Line(t, "c", "3", 1, 1) + "\n");
            Assert.Equal(32, usage.Refresh([root], now).BlockTokens);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("claude-opus-4-5-20251101", "opus-4-5")]
    [InlineData("claude-sonnet-4-5", "sonnet-4-5")]
    [InlineData("gpt-5", "gpt-5")]
    public void Model_names_are_shortened(string model, string expected)
    {
        Assert.Equal(expected, ClaudeUsage.ShortModel(model));
    }

    [Fact]
    public void Default_roots_respect_the_config_override()
    {
        Assert.Equal([Path.Combine("/a", "projects"), Path.Combine("/b", "projects")], ClaudeUsage.DefaultRoots(_ => "/a,/b", "/home/x"));
        Assert.Equal(2, ClaudeUsage.DefaultRoots(_ => null, "/home/x").Count);
    }
}

public class CodexUsageTests
{
    private const string Current = """{"timestamp":"2026-11-15T10:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":9000},"last_token_usage":{"input_tokens":800,"output_tokens":200,"total_tokens":1000}},"rate_limits":{"primary":{"used_percent":42.5,"window_minutes":300,"resets_at":1794751200},"secondary":{"used_percent":12,"window_minutes":10080,"resets_in_seconds":86400}}}}""";

    [Fact]
    public void Rate_limits_and_turn_tokens_are_read()
    {
        var usage = new CodexUsage();
        Assert.True(usage.Add(Current));
        Assert.False(usage.Add("""{"type":"event_msg","payload":{"type":"agent_message"}}"""));

        CodexUsageSummary summary = usage.Summarize(new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero));

        Assert.Equal(42.5, summary.Primary!.UsedPercent);
        Assert.Equal(300, summary.Primary.WindowMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794751200), summary.Primary.ResetsAt);
        Assert.Equal(12, summary.Secondary!.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), summary.Secondary.ResetsAt);
        Assert.Equal(1000, summary.WeekTokens);
    }

    [Fact]
    public void Older_layout_with_limits_inside_info_is_supported_and_newest_wins()
    {
        var usage = new CodexUsage();
        usage.Add(Current);
        usage.Add("""{"timestamp":"2026-11-15T10:30:00Z","type":"event_msg","payload":{"type":"token_count","info":{"rate_limits":{"primary":{"used_percent":60,"window_minutes":300}}}}}""");
        usage.Add("""{"timestamp":"2026-11-15T09:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300}}}}""");

        CodexUsageSummary summary = usage.Summarize(new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero));

        Assert.Equal(60, summary.Primary!.UsedPercent);
        Assert.Null(summary.Secondary);
    }

    [Fact]
    public void A_window_that_has_reset_reads_zero()
    {
        var usage = new CodexUsage();
        usage.Add(Current);

        CodexUsageSummary summary = usage.Summarize(DateTimeOffset.FromUnixTimeSeconds(1794751200).AddMinutes(1));

        Assert.Equal(0, summary.Primary!.UsedPercent);
        Assert.Null(summary.Primary.ResetsAt);
    }
}

public class WeatherAndFormatTests
{
    private const string Forecast = """
        {"current":{"temperature_2m":18.4,"apparent_temperature":17.1,"relative_humidity_2m":71,"weather_code":2,"is_day":1,"wind_speed_10m":12.3},
         "hourly":{"time":["2026-11-15T09:00","2026-11-15T10:00","2026-11-15T11:00","2026-11-15T12:00"],
                   "temperature_2m":[15,16,17.5,19],"precipitation_probability":[0,10,40,null]},
         "daily":{"time":["2026-11-15","2026-11-16"],"weather_code":[2,61],"temperature_2m_max":[20,17],"temperature_2m_min":[11,10]}}
        """;

    [Fact]
    public void Forecast_is_parsed_from_the_current_hour()
    {
        WeatherReport report = OpenMeteo.ParseForecast(Forecast, new DateTime(2026, 11, 15, 10, 20, 0), hours: 2);

        Assert.Equal(18.4, report.Temperature);
        Assert.Equal(2, report.Code);
        Assert.True(report.IsDay);
        Assert.Equal(71, report.Humidity);
        Assert.Equal([16.0, 17.5], report.Hours.Select(h => h.Temperature));
        Assert.Equal(40, report.Hours[1].RainChance);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal((10.0, 17.0, 61), (report.Days[1].Min, report.Days[1].Max, report.Days[1].Code));
    }

    [Fact]
    public void Codes_and_urls()
    {
        Assert.Equal("weather-clear-night", OpenMeteo.Describe(0, isDay: false).Icon);
        Assert.Equal("Trovoada", OpenMeteo.Describe(95).Description);
        Assert.Contains("latitude=38.7223&longitude=-9.1393", OpenMeteo.ForecastUrl(38.7223, -9.1393));
        Assert.EndsWith("name=S%C3%A3o%20Paulo", OpenMeteo.GeocodingUrl(" São Paulo "));
        Assert.Equal((38.72, -9.14, "Lisboa"), OpenMeteo.ParseGeocoding("""{"results":[{"name":"Lisboa","latitude":38.72,"longitude":-9.14}]}"""));
        Assert.Null(OpenMeteo.ParseGeocoding("""{"generationtime_ms":0.1}"""));
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1234, "1,2 k")]
    [InlineData(123_456, "123 k")]
    [InlineData(1_250_000, "1,25 M")]
    [InlineData(45_600_000, "45,6 M")]
    [InlineData(2_000_000_000, "2 B")]
    public void Compact_numbers(double value, string expected)
    {
        Assert.Equal(expected, TokenFormat.Compact(value, CultureInfo.GetCultureInfo("pt-PT")));
    }

    [Fact]
    public void Durations_until_a_time()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("agora", TokenFormat.Until(now, now));
        Assert.Equal("35 min", TokenFormat.Until(now.AddMinutes(34.2), now));
        Assert.Equal("2 h 10 min", TokenFormat.Until(now.AddMinutes(130), now));
        Assert.Equal("3 d 4 h", TokenFormat.Until(now.AddHours(76), now));
    }
}
