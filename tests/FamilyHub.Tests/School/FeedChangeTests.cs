using FamilyHub.Core.Configuration;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.School;

public sealed class FeedChangeTests : IDisposable
{
    /// <summary>The week on the screen from Tuesday 6 October 2026: Tuesday to Sunday.</summary>
    private static readonly DateTime From = new(2026, 10, 6);
    private static readonly DateTime To = new(2026, 10, 12);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 10, 0, TimeSpan.FromHours(2));

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 6, 6, 10, 0, TimeSpan.Zero));
    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly HubClock clock;
    private readonly AppDataPaths paths;

    public FeedChangeTests()
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

    private static FeedEvent Lesson(string key, int day, string from, string to, string title = "Statistik", string? place = "Fib 14") =>
        new(new DateTime(2026, 10, day).Add(TimeSpan.Parse(from)), new DateTime(2026, 10, day).Add(TimeSpan.Parse(to)), title, place, key);

    private static IReadOnlyList<FeedChange> Detect(IReadOnlyList<FeedEvent> before, IReadOnlyList<FeedEvent> after) =>
        FeedChanges.Detect(before, after, From, To, Now);

    // ------------------------------------------------------------------ what counts as a change

    [Fact]
    public void Moved_cancelled_new_and_new_room_are_found_in_this_week()
    {
        IReadOnlyList<FeedEvent> before =
        [
            Lesson("a", 6, "08:15", "12:00"),
            Lesson("b", 7, "10:15", "12:00", "Lineær algebra"),
            Lesson("c", 8, "08:15", "12:00", "Programmering", "Auditorium 2"),
        ];
        IReadOnlyList<FeedEvent> after =
        [
            Lesson("a", 6, "10:15", "12:00"),
            Lesson("c", 8, "08:15", "12:00", "Programmering", "Auditorium 1"),
            Lesson("d", 9, "12:45", "14:30", "Ekstra forelæsning", null),
        ];

        var changes = Detect(before, after);

        Assert.Equal(
            [
                "Tirsdag: Statistik er flyttet fra 08:15–12:00 til 10:15–12:00",
                "Onsdag: Lineær algebra 10:15–12:00 er aflyst",
                "Torsdag: Programmering 08:15–12:00 – nyt lokale: Auditorium 1",
                "Fredag: ny – Ekstra forelæsning 12:45–14:30",
            ],
            changes.Select(FeedChanges.Describe));
        Assert.Equal([FeedChangeKind.Moved, FeedChangeKind.Cancelled, FeedChangeKind.Changed, FeedChangeKind.Added], changes.Select(c => c.Kind));
        Assert.Equal(["Flyttet", "Aflyst", "Ændret", "Ny"], changes.Select(c => FeedChanges.Label(c.Kind)));
    }

    [Fact]
    public void Changes_outside_the_week_on_the_screen_and_on_days_gone_are_not_warned_about()
    {
        IReadOnlyList<FeedEvent> before = [Lesson("past", 5, "08:15", "12:00"), Lesson("later", 13, "08:15", "12:00")];
        IReadOnlyList<FeedEvent> after = [Lesson("later", 13, "10:15", "12:00")];

        Assert.Empty(Detect(before, after));
    }

    [Fact]
    public void A_new_uid_for_the_same_lesson_is_not_a_change_and_a_moved_one_is_still_moved()
    {
        IReadOnlyList<FeedEvent> before = [Lesson("old-1", 6, "08:15", "12:00"), Lesson("old-2", 7, "08:15", "10:00", "Kemi")];
        IReadOnlyList<FeedEvent> after = [Lesson("new-1", 6, "08:15", "12:00"), Lesson("new-2", 7, "12:45", "14:30", "Kemi")];

        var change = Assert.Single(Detect(before, after));
        Assert.Equal(FeedChangeKind.Moved, change.Kind);
        Assert.Equal("Onsdag: Kemi er flyttet fra 08:15–10:00 til 12:45–14:30", FeedChanges.Describe(change));
    }

    [Fact]
    public void A_lesson_moved_to_another_day_says_from_which_day()
    {
        var change = Assert.Single(Detect([Lesson("a", 6, "08:15", "12:00")], [Lesson("a", 8, "08:15", "12:00")]));

        Assert.Equal("Torsdag: Statistik er flyttet fra tirsdag 08:15–12:00 til 08:15–12:00", FeedChanges.Describe(change));
    }

    [Fact]
    public void A_lesson_moved_back_is_no_longer_a_change_and_the_first_time_is_kept()
    {
        var original = Lesson("a", 7, "08:15", "12:00");
        var first = FeedChanges.Merge([], Detect([original], [Lesson("a", 7, "10:15", "12:00")]), new DateOnly(2026, 10, 6));
        var second = FeedChanges.Merge(first, Detect([Lesson("a", 7, "10:15", "12:00")], [Lesson("a", 7, "12:45", "14:30")]), new DateOnly(2026, 10, 6));
        var back = FeedChanges.Merge(second, Detect([Lesson("a", 7, "12:45", "14:30")], [original]), new DateOnly(2026, 10, 6));

        Assert.Equal("Onsdag: Statistik er flyttet fra 08:15–12:00 til 12:45–14:30", FeedChanges.Describe(Assert.Single(second)));
        Assert.Empty(back);
    }

    [Fact]
    public void Changes_on_days_that_have_passed_go_away_by_themselves()
    {
        var pending = Detect([Lesson("a", 6, "08:15", "12:00")], [Lesson("a", 6, "10:15", "12:00")]);

        Assert.Single(FeedChanges.Merge(pending, [], new DateOnly(2026, 10, 6)));
        Assert.Empty(FeedChanges.Merge(pending, [], new DateOnly(2026, 10, 7)));
    }

    [Fact]
    public void Repeating_lessons_and_moved_occurrences_share_a_key()
    {
        var lessons = IcsParser.Parse("""
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:fysik
            DTSTART:20261005T061500Z
            DTEND:20261005T074500Z
            RRULE:FREQ=WEEKLY;COUNT=2
            SUMMARY:Fysik
            END:VEVENT
            BEGIN:VEVENT
            UID:fysik
            RECURRENCE-ID:20261012T061500Z
            DTSTART:20261012T100000Z
            DTEND:20261012T113000Z
            SUMMARY:Fysik
            END:VEVENT
            END:VCALENDAR
            """, DateTimeOffset.MinValue.AddYears(2025), DateTimeOffset.MaxValue.AddYears(-10), TimeZoneInfo.Utc);

        var series = new DateTimeOffset(2026, 10, 12, 6, 15, 0, TimeSpan.Zero).UtcTicks;
        Assert.Equal([$"fysik@{new DateTimeOffset(2026, 10, 5, 6, 15, 0, TimeSpan.Zero).UtcTicks}", $"fysik@{series}"], lessons.Select(l => l.Key));
    }

    // ------------------------------------------------------------------ the week and the service

    private static SchoolSchedule University() => new SchoolSchedule
    {
        Level = SchoolLevel.University,
        Grade = 3,
        Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
        FeedUrl = "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=x",
    }.Normalized();

    [Fact]
    public void A_changed_lesson_is_marked_and_a_cancelled_one_stays_struck_through()
    {
        var moved = Lesson("a", 6, "10:15", "12:00");
        IReadOnlyList<FeedChange> changes =
        [
            new("a", Lesson("a", 6, "08:15", "10:00"), moved, Now),
            new("b", Lesson("b", 7, "08:15", "10:00", "Kemi"), null, Now),
        ];

        var week = SchoolWeek.For(University(), new DateOnly(2026, 10, 5), [.. FeedTests.BusyAutumn(), moved], changes);
        var rows = week.Schedule.Periods.Where(p => !p.IsBreak).ToList();

        Assert.Equal(FeedChangeKind.Moved, week.LessonAt(DayOfWeek.Tuesday, rows[1]).Change);
        var cancelled = week.LessonAt(DayOfWeek.Wednesday, rows[0]);
        Assert.Equal(FeedChangeKind.Cancelled, cancelled.Change);
        Assert.Equal("Kemi", cancelled.Subject?.Name);
        Assert.Null(week.LessonAt(DayOfWeek.Monday, rows[0]).Change);
        Assert.Equal(2, week.Changes.Count);
    }

    [Fact]
    public async Task A_change_in_the_calendar_waits_until_it_is_marked_as_seen()
    {
        var http = new FeedTests.FakeHttp();
        var school = new SchoolScheduleService(paths, NullLogger<SchoolScheduleService>.Instance);
        var feeds = new ScheduleFeedService(http, school, paths, clock, NullLogger<ScheduleFeedService>.Instance);
        var schedule = await school.AddAsync(University());

        // The link is added: the starting point, nothing is a change.
        http.Respond(FeedTests.Moodle);
        await feeds.SaveAsync(schedule.Id, await feeds.FetchAsync(schedule.FeedUrl!));
        await feeds.RefreshAsync(schedule.Id);
        Assert.Empty(feeds.ChangesFor(schedule.Id));

        // Tuesday's exercises move from 08:15 to 10:15, Wednesday's lecture is cancelled.
        http.Respond(FeedTests.Moodle
            .Replace("DTSTART:20261006T061500Z", "DTSTART:20261006T081500Z")
            .Replace("UID:4@moodle.aau.dk", "UID:4@moodle.aau.dk\nSTATUS:CANCELLED"));
        await feeds.RefreshAsync(schedule.Id);

        Assert.Equal(
            ["Tirsdag: Statistik - øvelser er flyttet fra 08:15–12:00 til 10:15–12:00", "Onsdag: Lineær algebra 08:15–10:00 er aflyst"],
            feeds.ChangesFor(schedule.Id).Select(FeedChanges.Describe));

        // Still there after a restart – until someone marks them as seen.
        var restarted = new ScheduleFeedService(http, school, paths, clock, NullLogger<ScheduleFeedService>.Instance);
        Assert.Equal(2, restarted.ChangesFor(schedule.Id).Count);
        await restarted.AcknowledgeAsync(schedule.Id);
        Assert.Empty(restarted.ChangesFor(schedule.Id));
        Assert.Empty(new ScheduleFeedService(http, school, paths, clock, NullLogger<ScheduleFeedService>.Instance).ChangesFor(schedule.Id));
    }
}
