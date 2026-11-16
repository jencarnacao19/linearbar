using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LinearBar.Core.Calendar;

/// <summary>One occurrence of an event, ready to show.</summary>
public sealed record CalendarEvent
{
    public string Title { get; init; } = string.Empty;

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public bool AllDay { get; init; }

    public string? Location { get; init; }

    /// <summary>Teams/Meet/Zoom/Webex link found in the event, if any.</summary>
    public string? JoinUrl { get; init; }

    /// <summary>Name of the calendar it came from (when several are combined).</summary>
    public string? Calendar { get; init; }
}

/// <summary>
/// Reads iCalendar (RFC 5545) feeds — the "secret address in iCal format" Google Calendar
/// offers, or a published Outlook calendar — and lists the event occurrences in a time
/// range. Supports the parts real calendars use: time zones, all-day events, the common
/// recurrence rules (DAILY/WEEKLY/MONTHLY/YEARLY with INTERVAL, COUNT, UNTIL, BYDAY,
/// BYMONTHDAY), EXDATE, moved or cancelled single occurrences (RECURRENCE-ID).
/// </summary>
public static partial class ICalendar
{
    private const int MaxIterations = 20_000;

    /// <summary>All occurrences that overlap [<paramref name="from"/>, <paramref name="to"/>), sorted by start.</summary>
    public static List<CalendarEvent> Occurrences(string ics, DateTimeOffset from, DateTimeOffset to, string? calendarName = null)
    {
        List<VEvent> events = Parse(ics);

        // Single occurrences that were moved or cancelled, keyed by UID and original start.
        var overrides = new Dictionary<(string Uid, DateTimeOffset Original), VEvent>();
        foreach (VEvent e in events.Where(e => e.RecurrenceId is not null && e.Uid is not null))
        {
            overrides[(e.Uid!, e.RecurrenceId!.Value)] = e;
        }

        var result = new List<CalendarEvent>();
        foreach (VEvent e in events)
        {
            if (e.Start is null)
            {
                continue;
            }

            if (e.RecurrenceId is not null)
            {
                // An override stands on its own (unless cancelled).
                if (!e.Cancelled)
                {
                    AddIfOverlaps(result, e, e.Start.Value.Instant, from, to, calendarName);
                }

                continue;
            }

            if (e.Cancelled)
            {
                continue;
            }

            foreach (DateTimeOffset start in Expand(e, to))
            {
                if (e.ExDates.Contains(start) || (e.Uid is not null && overrides.ContainsKey((e.Uid, start))))
                {
                    continue;
                }

                AddIfOverlaps(result, e, start, from, to, calendarName);
            }
        }

        return result.OrderBy(r => r.Start).ThenBy(r => r.Title, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Finds an online-meeting link in free text.</summary>
    public static string? FindJoinUrl(params string?[] texts)
    {
        foreach (string? text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            Match match = JoinUrlPattern().Match(text);
            if (match.Success)
            {
                return match.Value.TrimEnd('.', ',', ')', '>', '"', '\'');
            }
        }

        return null;
    }

    [GeneratedRegex(@"https://(?:teams\.microsoft\.com/l/meetup-join/|teams\.live\.com/meet/|meet\.google\.com/|[\w.-]*zoom\.us/(?:j|my|w)/|[\w.-]*webex\.com/)[^\s""<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex JoinUrlPattern();

    private static void AddIfOverlaps(List<CalendarEvent> result, VEvent e, DateTimeOffset start, DateTimeOffset from, DateTimeOffset to, string? calendarName)
    {
        DateTimeOffset end = start + e.Duration;
        if (end <= from || start >= to)
        {
            // Zero-length events still count at their start.
            if (!(e.Duration == TimeSpan.Zero && start >= from && start < to))
            {
                return;
            }
        }

        result.Add(new CalendarEvent
        {
            Title = string.IsNullOrWhiteSpace(e.Summary) ? "(sem título)" : e.Summary!,
            Start = start,
            End = end,
            AllDay = e.Start!.Value.IsDate,
            Location = e.Location,
            JoinUrl = FindJoinUrl(e.Url, e.Location, e.Description),
            Calendar = calendarName,
        });
    }

    // ---- Recurrence ----------------------------------------------------------------------

    private static IEnumerable<DateTimeOffset> Expand(VEvent e, DateTimeOffset until)
    {
        ICalDate first = e.Start!.Value;
        if (e.Rule is not { } rule)
        {
            yield return first.Instant;
            yield break;
        }

        // Recurrence happens in wall-clock time of the event's zone (keeps 09:00 across DST).
        DateTime local = first.Local;
        TimeZoneInfo zone = first.Zone;
        int produced = 0;

        foreach (DateTime candidate in Candidates(local, rule))
        {
            DateTimeOffset instant = first.IsDate ? new DateTimeOffset(candidate.Date, zone.GetUtcOffset(candidate.Date)) : ToInstant(candidate, zone);
            if (rule.Until is { } stop && instant > stop)
            {
                yield break;
            }

            if (instant > until)
            {
                yield break;
            }

            if (candidate < local)
            {
                continue;
            }

            yield return instant;
            produced++;
            if (rule.Count is { } count && produced >= count)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<DateTime> Candidates(DateTime start, RRule rule)
    {
        TimeSpan time = start.TimeOfDay;
        int interval = Math.Max(1, rule.Interval);

        switch (rule.Frequency)
        {
            case "DAILY":
                for (int i = 0; i < MaxIterations; i++)
                {
                    DateTime day = start.Date.AddDays((long)i * interval);
                    if (rule.ByDay.Count == 0 || rule.ByDay.Any(d => d.Day == day.DayOfWeek))
                    {
                        yield return day + time;
                    }
                }

                break;

            case "WEEKLY":
                DayOfWeek[] days = rule.ByDay.Count > 0 ? rule.ByDay.Select(d => d.Day).Distinct().ToArray() : [start.DayOfWeek];
                DateTime weekStart = StartOfWeek(start.Date, rule.WeekStart);
                for (int i = 0; i < MaxIterations; i++)
                {
                    DateTime week = weekStart.AddDays(7L * i * interval);
                    foreach (DateTime day in days.Select(d => week.AddDays(((int)d - (int)rule.WeekStart + 7) % 7)).Order())
                    {
                        yield return day + time;
                    }
                }

                break;

            case "MONTHLY":
                var firstOfMonth = new DateTime(start.Year, start.Month, 1);
                for (int i = 0; i < MaxIterations; i++)
                {
                    DateTime month = firstOfMonth.AddMonths(i * interval);
                    foreach (DateTime day in MonthDays(month, rule, start.Day))
                    {
                        yield return day + time;
                    }
                }

                break;

            case "YEARLY":
                for (int i = 0; i < MaxIterations / 12; i++)
                {
                    int year = start.Year + i * interval;
                    if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(year))
                    {
                        continue;
                    }

                    yield return new DateTime(year, start.Month, start.Day) + time;
                }

                break;
        }
    }

    private static IEnumerable<DateTime> MonthDays(DateTime month, RRule rule, int defaultDay)
    {
        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var days = new SortedSet<int>();

        foreach (int d in rule.ByMonthDay)
        {
            int day = d > 0 ? d : daysInMonth + d + 1;
            if (day >= 1 && day <= daysInMonth)
            {
                days.Add(day);
            }
        }

        foreach ((int ordinal, DayOfWeek weekday) in rule.ByDay)
        {
            var matches = Enumerable.Range(1, daysInMonth).Where(d => new DateTime(month.Year, month.Month, d).DayOfWeek == weekday).ToList();
            if (ordinal == 0)
            {
                days.UnionWith(matches);
            }
            else
            {
                int index = ordinal > 0 ? ordinal - 1 : matches.Count + ordinal;
                if (index >= 0 && index < matches.Count)
                {
                    days.Add(matches[index]);
                }
            }
        }

        if (rule.ByMonthDay.Count == 0 && rule.ByDay.Count == 0 && defaultDay <= daysInMonth)
        {
            days.Add(defaultDay);
        }

        return days.Select(d => new DateTime(month.Year, month.Month, d));
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek weekStart) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));

    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        // Wall-clock times that do not exist (DST gap) move forward by the gap.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    // ---- Parsing ---------------------------------------------------------------------------

    private sealed class VEvent
    {
        public string? Uid;
        public string? Summary;
        public string? Location;
        public string? Description;
        public string? Url;
        public ICalDate? Start;
        public TimeSpan Duration;
        public RRule? Rule;
        public HashSet<DateTimeOffset> ExDates = [];
        public DateTimeOffset? RecurrenceId;
        public bool Cancelled;
    }

    private readonly record struct ICalDate(DateTime Local, TimeZoneInfo Zone, bool IsDate)
    {
        public DateTimeOffset Instant => IsDate
            ? new DateTimeOffset(Local.Date, Zone.GetUtcOffset(Local.Date))
            : ToInstant(Local, Zone);
    }

    private sealed class RRule
    {
        public string Frequency = "";
        public int Interval = 1;
        public int? Count;
        public DateTimeOffset? Until;
        public List<(int Ordinal, DayOfWeek Day)> ByDay = [];
        public List<int> ByMonthDay = [];
        public DayOfWeek WeekStart = DayOfWeek.Monday;
    }

    private static List<VEvent> Parse(string ics)
    {
        var events = new List<VEvent>();
        VEvent? current = null;
        ICalDate? end = null;
        TimeSpan? duration = null;

        foreach ((string name, Dictionary<string, string> parameters, string value) in Lines(ics))
        {
            switch (name)
            {
                case "BEGIN" when value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase):
                    current = new VEvent();
                    end = null;
                    duration = null;
                    continue;

                case "END" when value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && current is not null:
                    if (current.Start is { } start)
                    {
                        current.Duration = duration
                            ?? (end is { } e ? e.Instant - start.Instant : start.IsDate ? TimeSpan.FromDays(1) : TimeSpan.Zero);
                        if (current.Duration < TimeSpan.Zero)
                        {
                            current.Duration = TimeSpan.Zero;
                        }
                    }

                    events.Add(current);
                    current = null;
                    continue;
            }

            if (current is null)
            {
                continue;
            }

            switch (name)
            {
                case "UID":
                    current.Uid = value;
                    break;
                case "SUMMARY":
                    current.Summary = Unescape(value);
                    break;
                case "LOCATION":
                    current.Location = Unescape(value);
                    break;
                case "DESCRIPTION":
                    current.Description = Unescape(value);
                    break;
                case "URL":
                    current.Url = value;
                    break;
                case "STATUS":
                    current.Cancelled = value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
                    break;
                case "DTSTART":
                    current.Start = ParseDate(value, parameters);
                    break;
                case "DTEND":
                    end = ParseDate(value, parameters);
                    break;
                case "DURATION":
                    duration = ParseDuration(value);
                    break;
                case "RRULE":
                    current.Rule = ParseRule(value);
                    break;
                case "EXDATE":
                    foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (ParseDate(part, parameters) is { } ex)
                        {
                            current.ExDates.Add(ex.Instant);
                        }
                    }

                    break;
                case "RECURRENCE-ID":
                    current.RecurrenceId = ParseDate(value, parameters)?.Instant;
                    break;
            }
        }

        return events;
    }

    private static IEnumerable<(string Name, Dictionary<string, string> Parameters, string Value)> Lines(string ics)
    {
        // Unfold: a line starting with a space or tab continues the previous one.
        var unfolded = new List<string>();
        foreach (string raw in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && unfolded.Count > 0)
            {
                unfolded[^1] += raw[1..];
            }
            else
            {
                unfolded.Add(raw);
            }
        }

        foreach (string line in unfolded)
        {
            int colon = FindValueColon(line);
            if (colon <= 0)
            {
                continue;
            }

            string head = line[..colon];
            string value = line[(colon + 1)..];
            string[] parts = head.Split(';');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string parameter in parts.Skip(1))
            {
                int eq = parameter.IndexOf('=');
                if (eq > 0)
                {
                    parameters[parameter[..eq]] = parameter[(eq + 1)..].Trim('"');
                }
            }

            yield return (parts[0].ToUpperInvariant(), parameters, value);
        }
    }

    /// <summary>The colon that separates name/parameters from the value (parameters may be quoted and contain colons).</summary>
    private static int FindValueColon(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == ':' && !quoted)
            {
                return i;
            }
        }

        return -1;
    }

    private static ICalDate? ParseDate(string value, Dictionary<string, string> parameters)
    {
        value = value.Trim();
        bool isDate = parameters.TryGetValue("VALUE", out string? type) && type.Equals("DATE", StringComparison.OrdinalIgnoreCase)
                      || value.Length == 8;

        if (isDate)
        {
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                ? new ICalDate(date, TimeZoneInfo.Local, true)
                : null;
        }

        bool utc = value.EndsWith('Z');
        string digits = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(digits, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return null;
        }

        if (utc)
        {
            return new ICalDate(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeZoneInfo.Utc, false);
        }

        TimeZoneInfo zone = parameters.TryGetValue("TZID", out string? tzid) ? FindZone(tzid) : TimeZoneInfo.Local;
        return new ICalDate(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), zone, false);
    }

    /// <summary>Resolves IANA ("Europe/Lisbon") and Windows ("GMT Standard Time") zone names.</summary>
    public static TimeZoneInfo FindZone(string id)
    {
        string trimmed = id.Trim().Trim('"');

        // Some producers prefix the zone id with a path ("/mozilla.org/20050126_1/Europe/Lisbon").
        foreach (string candidate in new[] { trimmed, trimmed.TrimStart('/'), LastTwoSegments(trimmed) })
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(candidate, out string? iana))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(iana);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                }
            }
        }

        return TimeZoneInfo.Local;

        static string LastTwoSegments(string value)
        {
            string[] segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 ? segments[^2] + "/" + segments[^1] : string.Empty;
        }
    }

    private static TimeSpan? ParseDuration(string value)
    {
        Match match = DurationPattern().Match(value.Trim());
        if (!match.Success)
        {
            return null;
        }

        static int Number(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;
        var span = new TimeSpan(Number(match.Groups["w"]) * 7 + Number(match.Groups["d"]), Number(match.Groups["h"]), Number(match.Groups["m"]), Number(match.Groups["s"]));
        return match.Groups["neg"].Success ? -span : span;
    }

    [GeneratedRegex(@"^(?<neg>-)?\+?P(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+)S)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    private static RRule ParseRule(string value)
    {
        var rule = new RRule();
        foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = part[..eq].ToUpperInvariant();
            string v = part[(eq + 1)..];
            switch (key)
            {
                case "FREQ":
                    rule.Frequency = v.ToUpperInvariant();
                    break;
                case "INTERVAL" when int.TryParse(v, out int interval):
                    rule.Interval = interval;
                    break;
                case "COUNT" when int.TryParse(v, out int count):
                    rule.Count = count;
                    break;
                case "UNTIL":
                    rule.Until = ParseDate(v, new Dictionary<string, string>())?.Instant;
                    if (rule.Until is { } until && v.Trim().Length == 8)
                    {
                        // A date-only UNTIL includes that whole day.
                        rule.Until = until.AddDays(1).AddTicks(-1);
                    }

                    break;
                case "BYDAY":
                    foreach (string day in v.Split(','))
                    {
                        Match m = ByDayPattern().Match(day.Trim());
                        if (m.Success && Weekday(m.Groups["d"].Value) is { } weekday)
                        {
                            rule.ByDay.Add((m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : 0, weekday));
                        }
                    }

                    break;
                case "BYMONTHDAY":
                    rule.ByMonthDay.AddRange(v.Split(',').Select(d => int.TryParse(d, out int n) ? n : 0).Where(n => n != 0));
                    break;
                case "WKST" when Weekday(v) is { } weekStart:
                    rule.WeekStart = weekStart;
                    break;
            }
        }

        return rule;
    }

    [GeneratedRegex(@"^(?<n>[+-]?\d{1,2})?(?<d>MO|TU|WE|TH|FR|SA|SU)$", RegexOptions.IgnoreCase)]
    private static partial Regex ByDayPattern();

    private static DayOfWeek? Weekday(string code) => code.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null,
    };

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[++i];
                builder.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return builder.ToString().Trim();
    }
}
