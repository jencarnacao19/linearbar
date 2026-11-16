using System.Globalization;
using System.IO;
using System.Net.Http;
using LinearBar.Core.Calendar;
using LinearBar.Core.Plugins;
using LinearBar.Core.Usage;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// The next event and today's agenda from one or more iCalendar feeds — the "secret address
/// in iCal format" of a Google calendar, or a published Outlook/Microsoft 365 calendar —
/// with a button to join online meetings.
/// </summary>
/// <remarks>
/// Option <c>icsUrl</c>: one or more feed URLs separated by spaces, commas or semicolons
/// (local .ics files work too). Feeds are re-downloaded every 15 minutes; the widget itself
/// updates every 30 seconds so countdowns stay right.
/// </remarks>
internal sealed class CalendarPlugin : PollingPlugin
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan FeedRefresh = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, (DateTimeOffset Fetched, string Content)> _feeds = new(StringComparer.Ordinal);

    public override string Id => "calendar";

    public override string Name => "Calendário";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    protected override async Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        string[] urls = (Config.GetString("icsUrl") ?? string.Empty)
            .Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTimeOffset now = DateTimeOffset.Now;
        if (urls.Length == 0)
        {
            return new PluginUpdate(NotConfigured(now));
        }

        int days = (int)Math.Clamp(Config.GetNumber("days") ?? 2, 1, 14);
        DateTimeOffset todayStart = new(now.Date, now.Offset);
        var events = new List<CalendarEvent>();
        var errors = new List<string>();

        for (int i = 0; i < urls.Length; i++)
        {
            try
            {
                string ics = await FetchAsync(urls[i], now, cancellation).ConfigureAwait(false);
                events.AddRange(ICalendar.Occurrences(ics, todayStart, todayStart.AddDays(days), urls.Length > 1 ? $"Calendário {i + 1}" : null));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                errors.Add(ex.Message);
            }
        }

        if (events.Count == 0 && errors.Count > 0)
        {
            throw new InvalidOperationException("não foi possível ler o calendário: " + errors[0]);
        }

        return new PluginUpdate(Render(events.OrderBy(e => e.Start).ToList(), now));
    }

    internal static PluginOutput Render(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        List<CalendarEvent> timed = events.Where(e => !e.AllDay && e.End > now).ToList();
        CalendarEvent? current = timed.FirstOrDefault(e => e.Start <= now && e.End > now);
        CalendarEvent? next = timed.FirstOrDefault(e => e.Start > now);

        string text;
        string color = "#7C9CFF";
        string? joinUrl = null;

        if (current is not null)
        {
            text = "Agora: " + Shorten(current.Title, 22);
            color = "#4FD1A5";
            joinUrl = current.JoinUrl;
        }
        else if (next is not null && next.Start.ToLocalTime().Date == now.Date)
        {
            TimeSpan until = next.Start - now;
            text = until <= TimeSpan.FromMinutes(60)
                ? $"{TokenFormat.Until(next.Start, now)} · {Shorten(next.Title, 18)}"
                : $"{next.Start.ToLocalTime():HH:mm} {Shorten(next.Title, 20)}";
            if (until <= TimeSpan.FromMinutes(10))
            {
                color = "#FFB454";
            }

            if (until <= TimeSpan.FromMinutes(15))
            {
                joinUrl = next.JoinUrl;
            }
        }
        else
        {
            text = now.ToString("ddd d", culture);
        }

        var rows = new List<PluginRow>();
        foreach (IGrouping<DateTime, CalendarEvent> day in events.GroupBy(e => e.Start.ToLocalTime().Date))
        {
            string dayName = day.Key == now.Date ? "Hoje" : day.Key == now.Date.AddDays(1) ? "Amanhã" : culture.TextInfo.ToTitleCase(day.Key.ToString("dddd, d MMM", culture));
            rows.Add(new PluginRow { Icon = "calendar", Title = dayName, Color = "#A8A8B4" });

            foreach (CalendarEvent e in day)
            {
                bool past = e.End <= now;
                bool live = e.Start <= now && e.End > now;
                string when = e.AllDay ? "Todo o dia" : $"{e.Start.ToLocalTime():HH:mm}–{e.End.ToLocalTime():HH:mm}";
                rows.Add(new PluginRow
                {
                    Icon = e.JoinUrl is not null ? "video" : live ? "clock" : "dot",
                    Title = e.Title,
                    Detail = string.Join(" · ", new[] { e.Location is { Length: > 0 } l && !l.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? l : null, e.Calendar }.Where(s => s is not null)),
                    Value = when,
                    Color = past ? "#6C6C78" : live ? "#4FD1A5" : null,
                    Open = e.JoinUrl,
                });
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new PluginRow { Icon = "check", Title = "Sem eventos nos próximos dias" });
        }

        var buttons = new List<PluginAction>();
        if (joinUrl is not null)
        {
            buttons.Add(new PluginAction { Title = "Entrar na reunião", Icon = "video", Open = joinUrl });
        }

        buttons.Add(new PluginAction { Title = "Abrir calendário", Icon = "external", Open = "outlookcal:" });
        buttons.Add(new PluginAction { Title = "Atualizar", Icon = "refresh", Action = "refresh" });

        string tooltip = current is not null
            ? $"A decorrer: {current.Title} (até às {current.End.ToLocalTime():HH:mm})"
            : next is not null ? $"Próximo: {next.Title}\n{next.Start.ToLocalTime().ToString("ddd HH:mm", culture)}" : "Sem mais eventos hoje";

        return new PluginOutput
        {
            Text = text,
            Icon = "calendar",
            Color = color,
            Tooltip = tooltip,
            Panel = new PluginPanel
            {
                Title = culture.TextInfo.ToTitleCase(now.ToString("dddd, d 'de' MMMM", culture)),
                Subtitle = next is null && current is null ? "Nada mais hoje" : current is not null ? "A decorrer: " + current.Title : "A seguir: " + next!.Title,
                Rows = rows,
                Buttons = buttons,
            },
        };
    }

    private static PluginOutput NotConfigured(DateTimeOffset now)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        return new PluginOutput
        {
            Text = now.ToString("ddd d", culture),
            Icon = "calendar",
            Color = "#7C9CFF",
            Tooltip = "Calendário por configurar",
            Panel = new PluginPanel
            {
                Title = culture.TextInfo.ToTitleCase(now.ToString("dddd, d 'de' MMMM", culture)),
                Subtitle = "Ligue um calendário para ver os próximos eventos aqui.",
                Rows =
                [
                    new PluginRow { Icon = "globe", Title = "Google Calendar", Detail = "Definições do calendário → «Endereço secreto em formato iCal»" },
                    new PluginRow { Icon = "globe", Title = "Outlook / Microsoft 365", Detail = "Definições → Calendário → Calendários partilhados → Publicar → ICS" },
                    new PluginRow { Icon = "plug", Title = "Depois, nas definições do LinearBar", Detail = "\"plugins\": { \"calendar\": { \"icsUrl\": \"https://…\" } }" },
                ],
                Buttons =
                [
                    new PluginAction { Title = "Abrir calendário", Icon = "external", Open = "outlookcal:" },
                ],
            },
        };
    }

    private async Task<string> FetchAsync(string url, DateTimeOffset now, CancellationToken cancellation)
    {
        if (_feeds.TryGetValue(url, out var cached) && now - cached.Fetched < FeedRefresh)
        {
            return cached.Content;
        }

        string content;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "webcal")
        {
            string address = uri.Scheme == "webcal" ? "https" + url[6..] : url;
            content = await Http.GetStringAsync(address, cancellation).ConfigureAwait(false);
        }
        else
        {
            content = await File.ReadAllTextAsync(url, cancellation).ConfigureAwait(false);
        }

        _feeds[url] = (now, content);
        return content;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LinearBar/0.1");
        return client;
    }
}
