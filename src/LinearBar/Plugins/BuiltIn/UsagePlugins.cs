using System.Globalization;
using System.IO;
using LinearBar.Core.Plugins;
using LinearBar.Core.Usage;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// Claude Code usage from the local session logs: tokens in the current 5-hour block, when
/// the block resets, today and this week. Optional <c>blockTokenLimit</c> turns the gauge
/// into a percentage of a budget the user chooses (the plan's real limit is not in the logs).
/// </summary>
internal sealed class ClaudeUsagePlugin : PollingPlugin
{
    private readonly ClaudeUsage _usage = new();

    public override string Id => "claude";

    public override string Name => "Claude";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> roots = ClaudeUsage.DefaultRoots(Environment.GetEnvironmentVariable, home);
        if (!roots.Any(Directory.Exists) && Config.GetString("alwaysShow") != "true")
        {
            return Task.FromResult(new PluginUpdate(new PluginOutput { Hidden = true }));
        }

        DateTimeOffset now = DateTimeOffset.Now;
        ClaudeUsageSummary summary = _usage.Refresh(roots, now);
        return Task.FromResult(new PluginUpdate(Render(summary, now, Config.GetNumber("blockTokenLimit"))));
    }

    internal static PluginOutput Render(ClaudeUsageSummary summary, DateTimeOffset now, double? limit)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        string resets = summary.BlockEnd is { } end ? end.ToLocalTime().ToString("HH:mm", culture) : "—";
        string resetsIn = summary.BlockEnd is { } e ? TokenFormat.Until(e, now) : "—";

        double gauge;
        string gaugeLabel;
        if (limit is > 0)
        {
            gauge = summary.BlockTokens / limit.Value;
            gaugeLabel = $"{Math.Min(999, gauge * 100):0} % do orçamento";
        }
        else
        {
            gauge = summary.BlockStart is { } start ? (now - start).TotalMinutes / ClaudeUsage.BlockLength.TotalMinutes : 0;
            gaugeLabel = "janela de 5 h";
        }

        string text = limit is > 0 && summary.BlockStart is not null
            ? $"{Math.Min(999, gauge * 100):0} %"
            : TokenFormat.Compact(summary.BlockTokens, culture);

        var rows = new List<PluginRow>
        {
            new() { Icon = "claude", Title = "Bloco atual (5 h)", Detail = summary.BlockStart is null ? "sem atividade" : $"{summary.BlockMessages} mensagens", Value = TokenFormat.Compact(summary.BlockTokens, culture), Progress = Math.Clamp(gauge, 0, 1) },
            new() { Icon = "clock", Title = "Reinicia", Detail = summary.BlockEnd is null ? null : "daqui a " + resetsIn, Value = resets },
            new() { Icon = "calendar", Title = "Hoje", Value = TokenFormat.Compact(summary.TodayTokens, culture) },
            new() { Icon = "calendar", Title = "Últimos 7 dias", Value = TokenFormat.Compact(summary.WeekTokens, culture) },
        };

        foreach ((string model, long tokens) in summary.BlockModels.Take(3))
        {
            rows.Add(new PluginRow { Icon = "sparkle", Title = model, Detail = "neste bloco", Value = TokenFormat.Compact(tokens, culture) });
        }

        return new PluginOutput
        {
            Text = text,
            Icon = "claude",
            Color = "#D97757",
            Bars = [new PluginBar { Label = gaugeLabel, Value = Math.Clamp(gauge, 0, 1), Detail = "reinicia " + resets }],
            Graph = summary.Hourly,
            Tooltip = summary.BlockStart is null
                ? $"Claude Code\nSem bloco ativo\nHoje: {TokenFormat.Compact(summary.TodayTokens, culture)} tokens"
                : $"Claude Code\nBloco de 5 h: {TokenFormat.Compact(summary.BlockTokens, culture)} tokens\nReinicia às {resets} (daqui a {resetsIn})",
            Panel = new PluginPanel
            {
                Title = "Claude Code",
                Subtitle = "Uso local (registos do Claude Code)",
                Rows = rows,
                Buttons = [new PluginAction { Title = "Atualizar", Icon = "refresh", Action = "refresh" }],
            },
        };
    }
}

/// <summary>
/// Codex usage: the 5-hour and weekly rate-limit percentages Codex reports in its session
/// logs, plus tokens today and this week.
/// </summary>
internal sealed class CodexUsagePlugin : PollingPlugin
{
    private readonly CodexUsage _usage = new();

    public override string Id => "codex";

    public override string Name => "Codex";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = CodexUsage.DefaultRoot(Environment.GetEnvironmentVariable, home);
        if (!Directory.Exists(root) && Config.GetString("alwaysShow") != "true")
        {
            return Task.FromResult(new PluginUpdate(new PluginOutput { Hidden = true }));
        }

        DateTimeOffset now = DateTimeOffset.Now;
        return Task.FromResult(new PluginUpdate(Render(_usage.Refresh([root], now), now)));
    }

    internal static PluginOutput Render(CodexUsageSummary summary, DateTimeOffset now)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;

        static string WindowName(RateWindow window) => window.WindowMinutes switch
        {
            >= 10080 => "semana",
            >= 1440 => $"{window.WindowMinutes / 1440} d",
            > 0 => $"{window.WindowMinutes / 60.0:0.#} h",
            _ => "janela",
        };

        string Reset(RateWindow window) => window.ResetsAt is { } at ? "reinicia daqui a " + TokenFormat.Until(at, now) : "sem dados de reinício";

        var bars = new List<PluginBar>();
        var rows = new List<PluginRow>();
        foreach (RateWindow? window in new[] { summary.Primary, summary.Secondary })
        {
            if (window is null)
            {
                continue;
            }

            bars.Add(new PluginBar { Label = WindowName(window), Value = window.UsedPercent / 100, Detail = $"{window.UsedPercent:0} %" });
            rows.Add(new PluginRow { Icon = "gauge", Title = "Limite de " + WindowName(window), Detail = Reset(window), Value = $"{window.UsedPercent:0} %", Progress = window.UsedPercent / 100 });
        }

        rows.Add(new PluginRow { Icon = "calendar", Title = "Hoje", Value = TokenFormat.Compact(summary.TodayTokens, culture) + " tokens" });
        rows.Add(new PluginRow { Icon = "calendar", Title = "Últimos 7 dias", Value = TokenFormat.Compact(summary.WeekTokens, culture) + " tokens" });
        if (summary.ReportedAt is { } reported)
        {
            rows.Add(new PluginRow { Icon = "clock", Title = "Último registo", Value = reported.ToLocalTime().ToString("g", culture) });
        }

        string text = summary.Primary is { } p ? $"{p.UsedPercent:0} %" : TokenFormat.Compact(summary.TodayTokens, culture);

        return new PluginOutput
        {
            Text = text,
            Icon = "codex",
            Color = "#10A37F",
            Bars = bars,
            Tooltip = summary.Primary is null
                ? "Codex\nAinda sem dados de limites (use o Codex uma vez)"
                : "Codex\n" + string.Join("\n", rows.Take(bars.Count).Select(r => $"{r.Title}: {r.Value} ({r.Detail})")),
            Panel = new PluginPanel
            {
                Title = "Codex",
                Subtitle = "Limites reportados pelo Codex CLI",
                Rows = rows,
                Buttons = [new PluginAction { Title = "Atualizar", Icon = "refresh", Action = "refresh" }],
            },
        };
    }
}
