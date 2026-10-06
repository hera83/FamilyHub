using System.Globalization;
using Bunit;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Components;
using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Pages;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.School;

public sealed class SchoolPageTests : BunitContext
{
    /// <summary>Tuesday 6 October 2026 (week 41, odd) at 08:10 in Copenhagen – the 1st lesson is going on.</summary>
    private static readonly DateTimeOffset TuesdayMorning = new(2026, 10, 6, 6, 10, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(TuesdayMorning);
    private readonly HubClock clock;
    private readonly AppDataPaths paths;
    private readonly HouseholdService household;
    private readonly ToastService toasts = new(TimeProvider.System, NullLogger<ToastService>.Instance);
    private readonly FamilyMember emma = new() { Name = "Emma", Color = MemberColor.Sand };
    private readonly FamilyMember oliver = new() { Name = "Oliver", Color = MemberColor.Teal };

    public SchoolPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        household = new HouseholdService(paths, NullLogger<HouseholdService>.Instance);
        School = new SchoolScheduleService(paths, NullLogger<SchoolScheduleService>.Instance);

        Services.AddLogging();
        Services.AddSingleton<IToastService>(toasts);
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton<IHouseholdService>(household);
        Services.AddSingleton(School);
        Feeds = new ScheduleFeedService(Http, School, paths, clock, NullLogger<ScheduleFeedService>.Instance);
        Services.AddSingleton(Feeds);
    }

    private FeedTests.FakeHttp Http { get; } = new();

    private ScheduleFeedService Feeds { get; }

    private SchoolScheduleService School { get; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        clock.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task<(SchoolSchedule Emma, SchoolSchedule Oliver)> WithTwoChildrenAsync()
    {
        await household.UpdateAsync(h => h with { Members = [emma, oliver] });

        // Oliver is added first, but the tabs follow the family's order: Emma first.
        var olivers = await School.AddAsync(new SchoolSchedule
        {
            MemberId = oliver.Id,
            Grade = 0,
            ClassLetter = "B",
            Subjects = SubjectCatalog.For(SchoolLevel.PrimarySchool, 0),
            Periods = PeriodPlanner.StandardDay(),
        });
        var emmas = await School.AddAsync(ScheduleLogicTests.Sample(emma.Id));
        return (emmas, olivers);
    }

    [Fact]
    public async Task The_timetable_has_the_days_across_and_the_times_down_the_side()
    {
        await WithTwoChildrenAsync();

        var cut = Render<SchoolPage>();

        Assert.Equal(["Mandag", "Tirsdag", "Onsdag", "Torsdag", "Fredag"], cut.FindAll(".st-day__name").Select(e => e.TextContent));
        Assert.Equal("08:00–08:45", cut.FindAll(".st-time__range")[0].TextContent);
        Assert.Contains("Frikvarter", cut.FindAll(".st-break__label").Select(e => e.TextContent));
        Assert.Equal("Fri 14:15", cut.FindAll(".st-day__end")[0].TextContent);
        Assert.Equal("Lokale 64", cut.Find(".st-lesson__note").TextContent);
        Assert.Contains("Næste uge: Tysk", cut.Markup);
        Assert.Equal("Uge 41 · ulige uge", cut.Find(".hub-page__subtitle").TextContent);
    }

    [Fact]
    public async Task Today_and_the_lesson_going_on_are_marked()
    {
        await WithTwoChildrenAsync();

        var cut = Render<SchoolPage>();

        Assert.Equal("Tirsdag", cut.Find(".st-day--today .st-day__name").TextContent);
        Assert.Contains("Dansk", cut.Find(".st-cell--now").TextContent);
        Assert.Equal("1. time", cut.Find(".st-time--now .st-time__number").TextContent);
    }

    [Fact]
    public async Task One_tab_per_child_with_when_they_finish_today()
    {
        await WithTwoChildrenAsync();

        var cut = Render<SchoolPage>();
        var tabs = cut.FindAll(".st-tab");

        Assert.Equal(["Emma", "Oliver"], cut.FindAll(".st-tab__name").Select(e => e.TextContent));
        Assert.Equal("6.a · fri 09:30", cut.FindAll(".st-tab__meta")[0].TextContent);
        Assert.Equal("0.b · ingen skole", cut.FindAll(".st-tab__meta")[1].TextContent);
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));

        tabs[1].Click();

        Assert.Equal("true", cut.FindAll(".st-tab")[1].GetAttribute("aria-selected"));
        Assert.Empty(cut.FindAll(".st-lesson"));
    }

    [Fact]
    public async Task At_the_weekend_the_page_shows_next_week_without_today()
    {
        await WithTwoChildrenAsync();
        time.SetUtcNow(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

        var cut = Render<SchoolPage>();

        Assert.Equal("Næste uge · uge 42 · lige uge", cut.Find(".hub-page__subtitle").TextContent);
        Assert.Empty(cut.FindAll(".st-day--today"));
        Assert.Contains("Næste uge: Billedkunst", cut.Markup);
    }

    [Fact]
    public void Without_schedules_the_page_explains_how_to_start()
    {
        var cut = Render<SchoolPage>();

        Assert.Contains("Ingen skoleskemaer endnu", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void The_page_is_read_only()
    {
        var cut = Render<SchoolTimetable>(p => p.Add(t => t.View, SchoolWeek.For(ScheduleLogicTests.Sample(), new DateOnly(2026, 10, 5))));

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public async Task The_home_card_shows_where_each_child_is_in_the_day_then_tomorrow()
    {
        await WithTwoChildrenAsync();

        // Tuesday 08:10: Emma is in her first lesson; Oliver has no lessons today.
        var morning = Render<SchoolWidget>();
        Assert.Contains("Skole i dag", morning.Markup);
        Assert.Equal(["Fri 09:30", "Har fri"], morning.FindAll(".st-widget__time").Select(e => e.TextContent));
        Assert.Single(morning.FindAll(".st-widget__row--done"));


        time.SetUtcNow(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var afternoon = Render<SchoolWidget>();
        Assert.Contains("Skole i morgen", afternoon.Markup);
        Assert.Equal("08:00–09:30", afternoon.FindAll(".st-widget__time")[0].TextContent);

        time.SetUtcNow(new DateTimeOffset(2026, 10, 6, 19, 30, 0, TimeSpan.Zero));
        Assert.Empty(Render<SchoolWidget>().Markup.Trim());

        // Next Monday 09:40: in the break between the 2nd and 3rd lesson.
        time.SetUtcNow(new DateTimeOffset(2026, 10, 12, 7, 40, 0, TimeSpan.Zero));
        Assert.Equal("Pause til 09:50", Render<SchoolWidget>().FindAll(".st-widget__time")[0].TextContent);

        // Next Tuesday 07:30: before the first lesson.
        time.SetUtcNow(new DateTimeOffset(2026, 10, 13, 5, 30, 0, TimeSpan.Zero));
        Assert.Equal("Møder 08:00", Render<SchoolWidget>().FindAll(".st-widget__time")[0].TextContent);
    }

    [Fact]
    public async Task In_the_settings_a_subject_can_be_painted_onto_several_lessons()
    {
        var (schedule, _) = await WithTwoChildrenAsync();
        var third = schedule.Periods.Where(p => !p.IsBreak).ElementAt(2);

        var cut = Render<ScheduleEditorPage>(p => p.Add(e => e.Id, schedule.Id));
        cut.FindAll(".hub-choice__option").Single(b => b.TextContent.Trim() == "Matematik").Click();
        await cut.Find("[aria-label='Tirsdag, 3. time: ingen time']").ClickAsync(new());
        await cut.Find("[aria-label='Onsdag, 3. time: ingen time']").ClickAsync(new());

        var saved = School.Find(schedule.Id)!;
        var mat = saved.Subjects.Single(s => s.Name == "Matematik").Id;
        Assert.Equal(mat, saved.CellAt(DayOfWeek.Tuesday, third.Id)?.SubjectId);
        Assert.Equal(mat, saved.CellAt(DayOfWeek.Wednesday, third.Id)?.SubjectId);

        cut.FindAll(".hub-choice__option").Single(b => b.TextContent.Trim() == "Ryd").Click();
        await cut.Find("[aria-label='Onsdag, 3. time: Matematik']").ClickAsync(new());
        Assert.Null(School.Find(schedule.Id)!.CellAt(DayOfWeek.Wednesday, third.Id));
    }

    [Fact]
    public async Task Tapping_a_lesson_opens_it_with_subject_and_note()
    {
        var (schedule, _) = await WithTwoChildrenAsync();

        var cut = Render<ScheduleEditorPage>(p => p.Add(e => e.Id, schedule.Id));
        cut.Find("[aria-label='Mandag, 1. time: Dansk']").Click();

        Assert.Equal("Mandag · 1. time", cut.Find(".hub-dialog__title").TextContent);
        Assert.Equal("true", cut.FindAll(".hub-dialog .hub-choice__option").Single(b => b.TextContent.Trim() == "Dansk").GetAttribute("aria-checked"));
        Assert.Equal("Lokale 64", cut.Find(".hub-dialog input").GetAttribute("value"));
    }

    [Fact]
    public async Task Removing_a_subject_clears_its_lessons_and_can_be_undone()
    {
        var (schedule, _) = await WithTwoChildrenAsync();

        var cut = Render<ScheduleEditorPage>(p => p.Add(e => e.Id, schedule.Id));
        await cut.Find("[aria-label='Fjern Dansk']").ClickAsync(new());

        var removed = School.Find(schedule.Id)!;
        Assert.DoesNotContain(removed.Subjects, s => s.Name == "Dansk");
        var toast = Assert.Single(toasts.Visible);
        Assert.Equal("Dansk er fjernet", toast.Title);

        await toast.Action!.Callback();
        Assert.Contains(School.Find(schedule.Id)!.Subjects, s => s.Name == "Dansk");
        Assert.Equal(schedule.Cells.Count, School.Find(schedule.Id)!.Cells.Count);
    }

    [Fact]
    public async Task A_university_schedule_with_a_link_shows_this_weeks_lessons_from_the_calendar()
    {
        await household.UpdateAsync(h => h with { Members = [emma] });
        var schedule = await School.AddAsync(new SchoolSchedule
        {
            MemberId = emma.Id,
            Level = SchoolLevel.University,
            Grade = 3,
            Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
            FeedUrl = "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=hemmelig",
        });
        Http.Respond(FeedTests.Moodle);
        await Feeds.RefreshAsync(schedule.Id);

        var cut = Render<SchoolPage>();

        var lessons = cut.FindAll(".st-lesson__subject").Select(e => e.TextContent).ToList();
        Assert.Contains("Statistik - forelæsning 3", lessons);
        Assert.Contains("Fib 14, lokale 2.1", cut.Markup);
        Assert.Equal("3. semester · fri 16:15", cut.Find(".st-tab__meta").TextContent);
        Assert.DoesNotContain("hemmelig", cut.Markup);

        var settings = Render<SchoolSettingsPage>();
        Assert.Contains("fra www.moodle.aau.dk", settings.Markup);
        Assert.DoesNotContain("hemmelig", settings.Markup);
    }

    [Fact]
    public async Task When_the_calendar_has_failed_for_hours_the_page_says_so_calmly()
    {
        await household.UpdateAsync(h => h with { Members = [emma] });
        var schedule = await School.AddAsync(new SchoolSchedule
        {
            MemberId = emma.Id,
            Level = SchoolLevel.Gymnasium,
            Grade = 2,
            Periods = PeriodPlanner.StandardDay(SchoolLevel.Gymnasium),
            FeedUrl = "https://skole.example/kalender.ics",
        });
        Http.Respond(FeedTests.Moodle);
        await Feeds.RefreshAsync(schedule.Id);

        time.Advance(TimeSpan.FromHours(4));
        Http.Fail();
        await Feeds.RefreshAsync(schedule.Id);

        var cut = Render<SchoolPage>();
        Assert.Contains("Skemaet kunne ikke opdateres", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".st-lesson")); // the last known lessons stay
    }

    [Fact]
    public async Task A_change_this_week_is_shown_until_someone_marks_it_as_seen()
    {
        await household.UpdateAsync(h => h with { Members = [emma] });
        var schedule = await School.AddAsync(new SchoolSchedule
        {
            MemberId = emma.Id,
            Level = SchoolLevel.University,
            Grade = 3,
            Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
            FeedUrl = "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=hemmelig",
        });
        Http.Respond(FeedTests.Moodle);
        await Feeds.SaveAsync(schedule.Id, await Feeds.FetchAsync(schedule.FeedUrl!));
        Http.Respond(FeedTests.Moodle.Replace("UID:3@moodle.aau.dk", "UID:3@moodle.aau.dk\nLOCATION:Auditorium 3"));
        await Feeds.RefreshAsync(schedule.Id);

        var cut = Render<SchoolPage>();

        Assert.Contains("Skemaet er ændret i denne uge", cut.Find(".st-changes").TextContent);
        Assert.Contains("Tirsdag: Programmering (PBL) 12:45–16:15 – nyt lokale: Auditorium 3", cut.Find(".st-changes").TextContent);
        Assert.All(cut.FindAll(".st-lesson__badge"), b => Assert.Equal("Ændret", b.TextContent));
        Assert.NotEmpty(cut.FindAll(".st-lesson--changed"));
        Assert.NotNull(cut.Find(".st-tab__alert"));

        await cut.FindAll(".hub-btn").Single(b => b.TextContent.Trim() == "Markér som set").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".st-changes")));
        Assert.Empty(cut.FindAll(".st-lesson--changed"));
        Assert.Empty(cut.FindAll(".st-tab__alert"));
        Assert.Empty(Feeds.ChangesFor(schedule.Id));
    }

    [Fact]
    public async Task A_lesson_from_the_calendar_is_tapped_and_its_details_are_shown()
    {
        await household.UpdateAsync(h => h with { Members = [emma] });
        var schedule = await School.AddAsync(new SchoolSchedule
        {
            MemberId = emma.Id,
            Level = SchoolLevel.University,
            Grade = 3,
            Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
            FeedUrl = "https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=hemmelig",
        });
        Http.Respond(FeedTests.Moodle);
        await Feeds.SaveAsync(schedule.Id, await Feeds.FetchAsync(schedule.FeedUrl!));

        var cut = Render<SchoolPage>();
        cut.FindAll(".st-cell__press").First(p => p.TextContent.Contains("Statistik - øvelser")).Click();

        var dialog = cut.Find(".hub-dialog");
        Assert.Equal("Statistik - øvelser", dialog.QuerySelector(".hub-dialog__title")!.TextContent);
        Assert.Contains("Tirsdag 6. oktober · 08:15–12:00", dialog.TextContent);
        Assert.Equal(["Om timen", "Kursus", "Undervisere"], dialog.QuerySelectorAll(".sl-row__label").Select(l => l.TextContent));
        Assert.Contains("Bo Berg", dialog.TextContent);
        Assert.DoesNotContain("@", dialog.TextContent);
        Assert.DoesNotContain("https", dialog.TextContent);

        cut.Find(".hub-dialog [aria-label='Luk']").Click();
        Assert.Empty(cut.FindAll(".hub-dialog"));
    }

    [Fact]
    public async Task A_fixed_timetable_has_no_details_to_tap()
    {
        await WithTwoChildrenAsync();

        var cut = Render<SchoolPage>();

        Assert.NotEmpty(cut.FindAll(".st-lesson"));
        Assert.Empty(cut.FindAll(".st-cell__press"));
    }

    [Fact]
    public async Task With_a_link_the_schedule_is_bound_to_the_calendar_and_nothing_can_be_edited()
    {
        await household.UpdateAsync(h => h with { Members = [emma] });
        var schedule = await School.AddAsync(new SchoolSchedule
        {
            MemberId = emma.Id,
            Level = SchoolLevel.University,
            Grade = 3,
            Periods = PeriodPlanner.StandardDay(SchoolLevel.University),
            Subjects = [new SchoolSubject { Name = "Statistik" }],
        });
        Http.Respond(FeedTests.Moodle);

        var cut = Render<ScheduleEditorPage>(p => p.Add(e => e.Id, schedule.Id));
        Assert.Contains("Tider", cut.FindAll(".hub-section__title").Select(t => t.TextContent));
        Assert.NotEmpty(cut.FindAll(".st-cell__press"));

        // Add the link through the dialog.
        cut.FindAll(".hub-btn").Single(b => b.TextContent.Trim() == "Tilføj link").Click();
        cut.Find(".hub-dialog input").Input("https://www.moodle.aau.dk/calendar/export_execute.php?authtoken=hemmelig");
        await cut.FindAll(".hub-dialog .hub-btn").Single(b => b.TextContent.Trim() == "Hent skema").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".hub-dialog")));
        var titles = cut.FindAll(".hub-section__title").Select(t => t.TextContent).ToList();
        Assert.DoesNotContain("Tider", titles);
        Assert.DoesNotContain("Fag", titles);
        Assert.Empty(cut.FindAll(".st-cell__press"));
        Assert.DoesNotContain(cut.FindAll(".hub-btn"), b => b.TextContent.Contains("Tider fra kalenderen") || b.TextContent.Contains("Tilføj pause"));
        Assert.Contains("Timer og tider følger kalenderen", cut.Markup);
        Assert.Contains("12:45–16:15", cut.FindAll(".st-time__range").Select(t => t.TextContent));

        // The fixed timetable is untouched – it comes back if the link is removed.
        var saved = School.Find(schedule.Id)!;
        Assert.True(saved.UsesFeed);
        Assert.Equal(schedule.Periods.Select(ScheduleRules.TimeText), saved.Periods.Select(ScheduleRules.TimeText));
        Assert.Equal("Statistik", Assert.Single(saved.Subjects).Name);
    }
}
