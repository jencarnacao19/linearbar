using LinearBar.Core.Calendar;
using LinearBar.Core.GitHub;

namespace LinearBar.Core.Tests.Plugins;

public class ICalendarTests
{
    private static string Wrap(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", events) + "\r\nEND:VCALENDAR\r\n";

    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Simple_utc_event_with_folded_lines_and_escapes()
    {
        string ics = Wrap(
            "BEGIN:VEVENT",
            "UID:1",
            "SUMMARY:Reunião\\, equipa",
            " de produto",
            "DTSTART:20261116T100000Z",
            "DTEND:20261116T110000Z",
            "LOCATION:Sala 2",
            "DESCRIPTION:Entrar: https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0?context=x\\nObrigado",
            "END:VEVENT");

        CalendarEvent e = Assert.Single(ICalendar.Occurrences(ics, Monday, Monday.AddDays(1)));

        Assert.Equal("Reunião, equipade produto", e.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), e.Start);
        Assert.Equal(TimeSpan.FromHours(1), e.End - e.Start);
        Assert.False(e.AllDay);
        Assert.Equal("Sala 2", e.Location);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0?context=x", e.JoinUrl);
    }

    [Fact]
    public void Time_zones_are_honoured()
    {
        string ics = Wrap(
            "BEGIN:VEVENT", "UID:tz", "SUMMARY:Nova Iorque",
            "DTSTART;TZID=America/New_York:20261116T090000",
            "DURATION:PT30M",
            "END:VEVENT");

        CalendarEvent e = Assert.Single(ICalendar.Occurrences(ics, Monday, Monday.AddDays(1)));

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero), e.Start.ToUniversalTime());
        Assert.Equal(TimeSpan.FromMinutes(30), e.End - e.Start);
    }

    [Fact]
    public void Weekly_rule_with_byday_count_exdate_and_moved_occurrence()
    {
        string ics = Wrap(
            "BEGIN:VEVENT", "UID:standup", "SUMMARY:Standup",
            "DTSTART:20261102T090000Z", "DTEND:20261102T091500Z",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=12",
            "EXDATE:20261118T090000Z",
            "END:VEVENT",
            "BEGIN:VEVENT", "UID:standup", "SUMMARY:Standup (mudado)",
            "RECURRENCE-ID:20261120T090000Z",
            "DTSTART:20261120T150000Z", "DTEND:20261120T151500Z",
            "END:VEVENT");

        List<CalendarEvent> week = ICalendar.Occurrences(ics, Monday, Monday.AddDays(7));

        Assert.Equal(
            [new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)],
            week.Select(e => e.Start));
        Assert.Equal("Standup (mudado)", week[1].Title);

        // COUNT=12 from 2 Nov (Mon/Wed/Fri) ends on Fri 27 Nov.
        List<CalendarEvent> all = ICalendar.Occurrences(ics, Monday.AddDays(-30), Monday.AddDays(60));
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero), all[^1].Start);
        Assert.Equal(11, all.Count); // 12 minus the excluded one; the moved one is still there
    }

    [Fact]
    public void Monthly_nth_weekday_daily_until_and_yearly()
    {
        string ics = Wrap(
            "BEGIN:VEVENT", "UID:m", "SUMMARY:Último sexta", "DTSTART:20260130T120000Z", "RRULE:FREQ=MONTHLY;BYDAY=-1FR", "END:VEVENT",
            "BEGIN:VEVENT", "UID:d", "SUMMARY:Diário", "DTSTART:20261110T080000Z", "RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261118T235959Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:y", "SUMMARY:Aniversário", "DTSTART;VALUE=DATE:20001116", "RRULE:FREQ=YEARLY", "END:VEVENT");

        List<CalendarEvent> month = ICalendar.Occurrences(ics, Monday, Monday.AddDays(30));

        Assert.Contains(month, e => e.Title == "Último sexta" && e.Start.Day == 27 && e.Start.Month == 11);
        Assert.Equal([16, 18], month.Where(e => e.Title == "Diário").Select(e => e.Start.Day));
        CalendarEvent birthday = Assert.Single(month, e => e.Title == "Aniversário");
        Assert.True(birthday.AllDay);
        Assert.Equal(TimeSpan.FromDays(1), birthday.End - birthday.Start);
    }

    [Fact]
    public void Cancelled_and_out_of_range_events_are_skipped()
    {
        string ics = Wrap(
            "BEGIN:VEVENT", "UID:c", "SUMMARY:Cancelado", "STATUS:CANCELLED", "DTSTART:20261116T100000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:o", "SUMMARY:Antigo", "DTSTART:20250101T100000Z", "END:VEVENT");

        Assert.Empty(ICalendar.Occurrences(ics, Monday, Monday.AddDays(1)));
        Assert.Empty(ICalendar.Occurrences("not a calendar", Monday, Monday.AddDays(1)));
    }

    [Theory]
    [InlineData("Link: https://meet.google.com/abc-defg-hij.", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://us02web.zoom.us/j/123456?pwd=x", "https://us02web.zoom.us/j/123456?pwd=x")]
    [InlineData("nothing here https://example.com", null)]
    public void Join_links_are_found(string text, string? expected)
    {
        Assert.Equal(expected, ICalendar.FindJoinUrl(text));
    }

    [Fact]
    public void Zone_ids_in_several_spellings_are_resolved()
    {
        Assert.Equal(TimeSpan.Zero, ICalendar.FindZone("Europe/Lisbon").GetUtcOffset(new DateTime(2026, 1, 15)));
        Assert.Equal(TimeSpan.Zero, ICalendar.FindZone("/mozilla.org/20050126_1/Europe/Lisbon").GetUtcOffset(new DateTime(2026, 1, 15)));
        Assert.Equal(TimeSpan.FromHours(1), ICalendar.FindZone("W. Europe Standard Time").GetUtcOffset(new DateTime(2026, 1, 15)));
    }
}

public class GitHubDataTests
{
    [Fact]
    public void Notifications_are_parsed_and_urls_made_browsable()
    {
        const string json = """
            [
              { "id": "1", "unread": true, "reason": "review_requested", "updated_at": "2026-11-16T09:00:00Z",
                "subject": { "title": "Fix bar", "type": "PullRequest", "url": "https://api.github.com/repos/o/r/pulls/12" },
                "repository": { "full_name": "o/r" } },
              { "id": "2", "unread": false, "reason": "mention", "subject": { "title": "Old", "type": "Issue", "url": "" }, "repository": { "full_name": "o/r" } },
              { "id": "3", "unread": true, "reason": "subscribed",
                "subject": { "title": "v1.0", "type": "Release", "url": "https://api.github.com/repos/o/r/releases/99" },
                "repository": { "full_name": "o/r" } }
            ]
            """;

        List<GitHubNotification> list = GitHubData.ParseNotifications(json);

        Assert.Equal(2, list.Count);
        Assert.Equal("https://github.com/o/r/pull/12", list[0].WebUrl);
        Assert.Equal("revisão pedida", GitHubData.DescribeReason(list[0].Reason));
        Assert.Equal("https://github.com/o/r/releases", list[1].WebUrl);
    }

    [Fact]
    public void Search_results_are_parsed()
    {
        const string json = """
            { "total_count": 1, "items": [ { "title": "Add plugins", "number": 7, "html_url": "https://github.com/o/r/pull/7",
              "repository_url": "https://api.github.com/repos/o/r", "updated_at": "2026-11-15T10:00:00Z", "draft": true } ] }
            """;

        GitHubItem item = Assert.Single(GitHubData.ParseSearch(json));

        Assert.Equal(("Add plugins", "o/r", 7, true), (item.Title, item.Repository, item.Number, item.IsDraft));
        Assert.Empty(GitHubData.ParseSearch("""{ "message": "Bad credentials" }"""));
    }

    [Fact]
    public void Commit_and_missing_urls()
    {
        Assert.Equal("https://github.com/o/r/commit/abc", GitHubData.WebUrl("https://api.github.com/repos/o/r/commits/abc", "o/r", "Commit"));
        Assert.Equal("https://github.com/o/r", GitHubData.WebUrl("", "o/r", "CheckSuite"));
    }
}
