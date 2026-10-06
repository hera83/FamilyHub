using System.Net;
using System.Text;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.School;

public sealed class FeedTests : IDisposable
{
    /// <summary>Like Moodle's export: UTC times (CEST is UTC+2 in October), a deadline without length and an all-day event.</summary>
    internal const string Moodle = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Moodle Pty Ltd//NONSGML Moodle Version 2024100700//EN
        BEGIN:VEVENT
        UID:1@moodle.aau.dk
        SUMMARY:Statistik - forelæsning 3
        LOCATION:Fib 14\, lokale 2.1
        DTSTART:20261005T061500Z
        DTEND:20261005T100000Z
        CATEGORIES:STAT-2026
        END:VEVENT
        BEGIN:VEVENT
        UID:2@moodle.aau.dk
        SUMMARY:Statistik - øvelser
        DTSTART:20261006T061500Z
        DTEND:20261006T100000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:3@moodle.aau.dk
        SUMMARY:Programmering (PBL)
        DTSTART:20261006T104500Z
        DTEND:20261006T141500Z
        END:VEVENT
        BEGIN:VEVENT
        UID:4@moodle.aau.dk
        SUMMARY:Lineær algebra
        DTSTART:20261007T061500Z
        DTEND:20261007T080000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:5@moodle.aau.dk
        SUMMARY:Studiegruppe
        DTSTART:20261008T150000Z
        DTEND:20261008T170000Z
        END:VEVENT
        BEGIN:VEVENT
        UID:6@moodle.aau.dk
        SUMMARY:Aflevering 2 skal afleveres
        DTSTART:20261009T215900Z
        DTEND:20261009T215900Z
        END:VEVENT
        BEGIN:VEVENT
        UID:7@moodle.aau.dk
        SUMMARY:Rusfest
        DTSTART;VALUE=DATE:20261009
        DTEND;VALUE=DATE:20261010
        END:VEVENT
        BEGIN:VEVENT
        UID:8@moodle.aau.dk
        SUMMARY:Statistik - forelæsning 4
        DTSTART:20261012T061500Z
        DTEND:20261012T100000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 6 October 2026, 08:10 in Copenhagen.</summary>
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 6, 6, 10, 0, TimeSpan.Zero));
    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly HubClock clock;
    private readonly AppDataPaths paths;

    public FeedTests()
    {
        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
    }

    public void Dispose()
    {
        clock.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A school server in a box: answers with a calendar, a status code or a dropped connection.</summary>
    internal sealed class FakeHttp : IHttpClientFactory
    {
        private Func<HttpRequestMessage, HttpResponseMessage> respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<Uri> Requests { get; } = [];

        public void Respond(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            respond = _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") };

        public void Fail() => respond = _ => throw new HttpRequestException("Ingen forbindelse");

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(FakeHttp owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests.Add(request.RequestUri!);
                return Task.FromResult(owner.respond(request));
            }
        }
    }

    private static IReadOnlyList<IcsOccurrence> Parse(string ics) => IcsParser.Parse(ics, From, To, Copenhagen);

    private static string Calendar(string events) => $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\n{events.Trim()}\r\nEND:VCALENDAR\r\n";

    // ------------------------------------------------------------------ reading iCalendar

    [Fact]
    public void Moodle_lessons_are_read_and_deadlines_and_all_day_events_left_out()
    {
        var lessons = Parse(Moodle);

        Assert.Equal(6, lessons.Count);
        Assert.DoesNotContain(lessons, l => l.Title.StartsWith("Aflevering", StringComparison.Ordinal) || l.Title == "Rusfest");
        var first = lessons[0];
        Assert.Equal("Statistik - forelæsning 3", first.Title);
        Assert.Equal("Fib 14, lokale 2.1", first.Location);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 8, 15, 0, TimeSpan.FromHours(2)), first.Start);
        Assert.Equal(TimeSpan.FromMinutes(225), first.End - first.Start);
    }

    [Fact]
    public void Folded_lines_escapes_and_time_zones_are_understood()
    {
        var lessons = Parse(Calendar("""
            BEGIN:VEVENT
            SUMMARY:Dansk\, litteratur
             og sprog
            DTSTART;TZID=Europe/Copenhagen:20261026T081500
            DURATION:PT1H30M
            BEGIN:VALARM
            DTSTART:20260101T000000Z
            END:VALARM
            END:VEVENT
            BEGIN:VEVENT
            SUMMARY:Aflyst
            STATUS:CANCELLED
            DTSTART:20261026T100000Z
            DTEND:20261026T110000Z
            END:VEVENT
            """));

        var lesson = Assert.Single(lessons);
        Assert.Equal("Dansk, litteraturog sprog", lesson.Title);

        // 26 October is after the change to winter time: 08:15 in Copenhagen is 07:15 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 7, 15, 0, TimeSpan.Zero), lesson.Start.ToUniversalTime());
        Assert.Equal(TimeSpan.FromMinutes(90), lesson.End - lesson.Start);
    }

    [Fact]
    public void Weekly_repetition_keeps_the_wall_clock_and_skips_exceptions()
    {
        var lessons = Parse(Calendar("""
            BEGIN:VEVENT
            UID:fysik
            SUMMARY:Fysik
            DTSTART;TZID=Europe/Copenhagen:20261019T081500
            DTEND;TZID=Europe/Copenhagen:20261019T094500
            RRULE:FREQ=WEEKLY;BYDAY=MO,TH;COUNT=6
            EXDATE;TZID=Europe/Copenhagen:20261022T081500
            END:VEVENT
            BEGIN:VEVENT
            UID:fysik
            RECURRENCE-ID;TZID=Europe/Copenhagen:20261026T081500
            SUMMARY:Fysik (flyttet)
            DTSTART;TZID=Europe/Copenhagen:20261026T120000
            DTEND;TZID=Europe/Copenhagen:20261026T133000
            END:VEVENT
            """));

        var local = lessons.Select(l => (l.Title, Start: TimeZoneInfo.ConvertTime(l.Start, Copenhagen).DateTime)).ToList();
        Assert.Equal(
        [
            ("Fysik", new DateTime(2026, 10, 19, 8, 15, 0)),
            ("Fysik (flyttet)", new DateTime(2026, 10, 26, 12, 0, 0)),
            ("Fysik", new DateTime(2026, 10, 29, 8, 15, 0)), // still 08:15 after the change to winter time
            ("Fysik", new DateTime(2026, 11, 2, 8, 15, 0)),
            ("Fysik", new DateTime(2026, 11, 5, 8, 15, 0)),
        ], local);
    }

    [Fact]
    public void Repetition_stops_at_until()
    {
        var lessons = Parse(Calendar("""
            BEGIN:VEVENT
            SUMMARY:Kemi
            DTSTART:20261005T080000Z
            DTEND:20261005T093000Z
            RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=20261031T235959Z
            END:VEVENT
            """));

        Assert.Equal([5, 19], lessons.Select(l => l.Start.Day));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Log ind</body></html>")]
    [InlineData("")]
    public void Something_else_than_a_calendar_is_refused(string text) =>
        Assert.Throws<IcsFormatException>(() => Parse(text));

    [Theory]
    [InlineData("PT45M", 45)]
    [InlineData("PT1H30M", 90)]
    [InlineData("P1DT1H", 25 * 60)]
    [InlineData("nonsens", 0)]
    public void Durations_are_read(string text, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), IcsParser.ParseDuration(text));

    // ------------------------------------------------------------------ rows and the week

    private static IReadOnlyList<FeedEvent> Local(IEnumerable<IcsOccurrence> lessons) =>
        [.. lessons.Select(l => new FeedEvent(TimeZoneInfo.ConvertTime(l.Start, Copenhagen).DateTime, TimeZoneInfo.ConvertTime(l.End, Copenhagen).DateTime, l.Title, l.Location))];

    [Fact]
    public void Rows_are_made_from_the_most_common_lesson_times()
    {
        var rows = PeriodPlanner.FromEvents(Local(Parse(Moodle)))!;

        var lessons = rows.Where(r => !r.IsBreak).Select(r => ScheduleRulesText(r)).ToList();
        Assert.Equal(["08:15–12:00", "12:45–16:15", "17:00–19:00"], lessons);
        Assert.All(rows.Where(r => r.IsBreak), r => Assert.Equal("Pause", r.Label)); // every gap, also around noon
        Assert.Equal("Pause", rows[3].Label);
        Assert.Null(PeriodPlanner.FromEvents([]));
    }

    [Fact]
    public void A_single_meeting_does_not_become_a_row_in_a_busy_calendar()
    {
        var monday = new DateTime(2026, 10, 5);
        var events = Enumerable.Range(0, 8)
            .SelectMany(w => new[]
            {
                new FeedEvent(monday.AddDays(w * 7).AddHours(8.25), monday.AddDays(w * 7).AddHours(12), "Statistik", null),
                new FeedEvent(monday.AddDays(w * 7 + 1).AddHours(12.75), monday.AddDays(w * 7 + 1).AddHours(16.25), "Programmering", null),
            })
            .Append(new FeedEvent(monday.AddDays(3).AddHours(17), monday.AddDays(3).AddHours(19), "Studiemøde", null))
            .ToList();

        var rows = PeriodPlanner.FromEvents(events)!;

        Assert.Equal(["08:15–12:00", "12:45–16:15"], rows.Where(r => !r.IsBreak).Select(ScheduleRules.TimeText));
    }

    private static string ScheduleRulesText(SchoolPeriod period) => ScheduleRules.TimeText(period);

    private static SchoolSchedule University(IReadOnlyList<SchoolSubject>? subjects = null) => new SchoolSchedule
    {
        Level = SchoolLevel.University,
        Grade = 3,
        Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
        Subjects = subjects ?? [],
        FeedUrl = "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=x",
    }.Normalized();

    [Fact]
    public void With_a_link_the_rows_follow_the_calendar_and_the_fixed_ones_are_kept()
    {
        var schedule = University();
        var week = SchoolWeek.For(schedule, new DateOnly(2026, 10, 5), Local(Parse(Moodle)));
        var rows = week.Schedule.Periods.Where(p => !p.IsBreak).ToList();

        Assert.True(week.IsLive);
        Assert.Equal(["08:15–12:00", "12:45–16:15", "17:00–19:00"], rows.Select(ScheduleRules.TimeText));
        Assert.Equal(PeriodPlanner.StandardDay(SchoolLevel.University).Select(ScheduleRules.TimeText), schedule.Periods.Select(ScheduleRules.TimeText));

        Assert.Equal("Statistik - forelæsning 3", week.LessonAt(DayOfWeek.Monday, rows[0]).Subject?.Name);
        Assert.Null(week.LessonAt(DayOfWeek.Monday, rows[0]).Time);
        Assert.Equal("Fib 14, lokale 2.1", week.LessonAt(DayOfWeek.Monday, rows[0]).Note);
        Assert.Equal("08:15–10:00", week.LessonAt(DayOfWeek.Wednesday, rows[0]).Time);
        Assert.Equal("Programmering (PBL)", week.LessonAt(DayOfWeek.Tuesday, rows[1]).Subject?.Name);
        Assert.True(week.LessonAt(DayOfWeek.Monday, rows[1]).IsEmpty);
        Assert.Equal(new SchoolDaySpan(new(8, 15), new(16, 15)), week.DaySpan(DayOfWeek.Tuesday));
        Assert.Null(week.DaySpan(DayOfWeek.Friday));
        Assert.Empty(week.Outside);
        Assert.Equal(5, week.LessonCount);
    }

    /// <summary>Eight weeks of exercises 08:15–10:00 and lectures 10:15–12:00 on Mondays and Thursdays.</summary>
    internal static List<FeedEvent> BusyAutumn()
    {
        var monday = new DateTime(2026, 10, 5);
        return [.. Enumerable.Range(0, 8).SelectMany(w => new[] { 0, 3 }.SelectMany(d => new[]
        {
            new FeedEvent(monday.AddDays(w * 7 + d).AddHours(8.25), monday.AddDays(w * 7 + d).AddHours(10), "Øvelser", null, $"o{w}{d}"),
            new FeedEvent(monday.AddDays(w * 7 + d).AddHours(10.25), monday.AddDays(w * 7 + d).AddHours(12), "Forelæsning", null, $"f{w}{d}"),
        }))];
    }

    [Fact]
    public void A_long_lecture_shows_in_every_row_it_overlaps_and_an_odd_one_is_listed_under_the_table()
    {
        var events = BusyAutumn();
        events.Add(new FeedEvent(new DateTime(2026, 10, 7, 8, 15, 0), new DateTime(2026, 10, 7, 12, 0, 0), "Temadag", "Aula"));
        events.Add(new FeedEvent(new DateTime(2026, 10, 8, 17, 0, 0), new DateTime(2026, 10, 8, 19, 0, 0), "Studiemøde", null));

        var week = SchoolWeek.For(University(), new DateOnly(2026, 10, 5), events);
        var rows = week.Schedule.Periods.Where(p => !p.IsBreak).ToList();

        Assert.Equal(["08:15–10:00", "10:15–12:00"], rows.Select(ScheduleRules.TimeText));
        Assert.Equal("Temadag", week.LessonAt(DayOfWeek.Wednesday, rows[0]).Subject?.Name);
        Assert.Equal("Temadag", week.LessonAt(DayOfWeek.Wednesday, rows[1]).Subject?.Name);
        Assert.Equal("08:15–12:00", week.LessonAt(DayOfWeek.Wednesday, rows[1]).Time);
        Assert.Equal("Studiemøde", Assert.Single(week.Outside).Title);
    }

    [Fact]
    public void An_empty_calendar_keeps_the_schedules_own_rows()
    {
        var schedule = University();

        var week = SchoolWeek.For(schedule, new DateOnly(2026, 10, 5), []);

        Assert.Equal(schedule.Periods, week.Schedule.Periods);
    }

    [Fact]
    public void Calendar_lessons_get_a_subjects_colour_or_a_steady_one_of_their_own()
    {
        var statistik = new SchoolSubject { Name = "Statistik", Color = MemberColor.Plum };
        var week = SchoolWeek.For(University([statistik]), new DateOnly(2026, 10, 5), []);

        Assert.Equal(MemberColor.Plum, week.SubjectFor("Statistik - øvelser").Color);
        Assert.Equal(week.SubjectFor("Programmering (PBL)").Color, week.SubjectFor("Programmering - forelæsning").Color);
        Assert.Equal("programmering", SchoolWeek.CourseKey("Programmering (PBL)"));
    }

    [Fact]
    public void Without_a_link_the_week_is_the_fixed_timetable()
    {
        var schedule = ScheduleLogicTests.Sample();
        var week = SchoolWeek.For(schedule, new DateOnly(2026, 10, 5), Local(Parse(Moodle)));

        Assert.False(week.IsLive);
        Assert.Equal("Dansk", week.LessonAt(DayOfWeek.Monday, schedule.Periods[0]).Subject?.Name);

        // A folkeskole schedule never uses a link, even if one is left in the file.
        Assert.False((schedule with { FeedUrl = "https://x.dk/a.ics" }).UsesFeed);
    }

    // ------------------------------------------------------------------ fetching

    private (ScheduleFeedService Feeds, SchoolScheduleService School, FakeHttp Http) Create(FakeHttp? http = null)
    {
        http ??= new FakeHttp();
        var school = new SchoolScheduleService(paths, NullLogger<SchoolScheduleService>.Instance);
        return (new ScheduleFeedService(http, school, paths, clock, NullLogger<ScheduleFeedService>.Instance), school, http);
    }

    [Fact]
    public async Task A_link_is_fetched_and_the_lessons_are_in_the_households_time()
    {
        var (feeds, _, http) = Create();
        http.Respond(Moodle);

        var lessons = await feeds.FetchAsync("webcal://www.moodle.aau.dk/calendar/export_execute.php?authtoken=x");

        Assert.Equal("https", http.Requests.Single().Scheme);
        Assert.Equal(new DateTime(2026, 10, 5, 8, 15, 0), lessons[0].Start);
    }

    [Theory]
    [InlineData("https://www.moodle.aau.dk/calendar/export_execute.php?userid=1&authtoken=x&preset_what=all&preset_time=weeknow",
                "https://www.moodle.aau.dk/calendar/export_execute.php?userid=1&authtoken=x&preset_what=all&preset_time=recentupcoming")]
    [InlineData("webcal://moodle.example/calendar/export_execute.php?preset_time=monthnow&authtoken=x",
                "https://moodle.example/calendar/export_execute.php?preset_time=recentupcoming&authtoken=x")]
    [InlineData("https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=x&preset_time=custom",
                "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=x&preset_time=custom")]
    [InlineData("https://skole.example/kalender.ics?preset_time=weeknow", "https://skole.example/kalender.ics?preset_time=weeknow")]
    public void A_moodle_link_for_one_week_or_month_is_fetched_as_the_next_60_days(string link, string fetched)
    {
        Assert.True(ScheduleFeedService.TryNormalize(link, out var uri));
        Assert.Equal(fetched, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, FeedProblem.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, FeedProblem.Rejected)]
    [InlineData(HttpStatusCode.BadGateway, FeedProblem.Offline)]
    public async Task A_link_that_fails_gets_a_problem_and_a_danish_message(HttpStatusCode status, FeedProblem problem)
    {
        var (feeds, _, http) = Create();
        http.Respond("", status);

        var error = await Assert.ThrowsAsync<FeedException>(() => feeds.FetchAsync("https://skole.example/a.ics"));

        Assert.Equal(problem, error.Problem);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public async Task A_login_page_or_a_wrong_link_is_explained()
    {
        var (feeds, _, http) = Create();
        http.Respond("<html>Log ind</html>");

        Assert.Equal(FeedProblem.NotCalendar, (await Assert.ThrowsAsync<FeedException>(() => feeds.FetchAsync("https://skole.example/a"))).Problem);
        Assert.Equal(FeedProblem.InvalidLink, (await Assert.ThrowsAsync<FeedException>(() => feeds.FetchAsync("ftp://skole.example/a"))).Problem);
        Assert.Equal("kalender-linket", ScheduleFeedService.HostOf("ikke et link"));
        Assert.Equal("www.moodle.aau.dk", ScheduleFeedService.HostOf("webcal://www.moodle.aau.dk/x?authtoken=y"));
    }

    [Fact]
    public async Task Fetched_lessons_survive_a_restart_and_a_failure_keeps_them()
    {
        var (feeds, school, http) = Create();
        var schedule = await school.AddAsync(University());
        http.Respond(Moodle);
        await feeds.RefreshAsync(schedule.Id);

        http.Fail();
        await feeds.RefreshAsync(schedule.Id);
        Assert.Equal(FeedProblem.Offline, feeds.StatusOf(schedule.Id).Problem);
        Assert.Equal(6, feeds.EventsFor(schedule.Id).Count);

        var (restarted, _, _) = Create(http);
        Assert.Equal(6, restarted.EventsFor(schedule.Id).Count);
        Assert.NotNull(restarted.StatusOf(schedule.Id).LastSuccess);
    }

    [Fact]
    public async Task Calendars_are_fetched_every_half_hour_and_sooner_after_a_failure()
    {
        var (feeds, school, http) = Create();
        await school.AddAsync(University());
        await school.AddAsync(ScheduleLogicTests.Sample()); // no link – never fetched
        http.Respond(Moodle);

        await feeds.RefreshDueAsync();
        await feeds.RefreshDueAsync();
        Assert.Single(http.Requests);

        time.Advance(ScheduleFeedService.RefreshInterval);
        http.Fail();
        await feeds.RefreshDueAsync();
        Assert.Equal(2, http.Requests.Count);

        time.Advance(ScheduleFeedService.RetryInterval);
        await feeds.RefreshDueAsync();
        Assert.Equal(3, http.Requests.Count);
    }
}
