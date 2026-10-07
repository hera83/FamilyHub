using FamilyHub.Core.Configuration;
using FamilyHub.Core.Storage;
using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.School;

public sealed class LessonAdjustmentTests : IDisposable
{
    /// <summary>Week 41 – Tuesday 6 October has Dansk (08:00–08:45) and Matematik (08:45–09:30) in the sample.</summary>
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Tuesday = new(2026, 10, 6);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static List<SchoolPeriod> Lessons(SchoolSchedule schedule) => [.. schedule.Periods.Where(p => !p.IsBreak)];

    private static SchoolSchedule Hide(SchoolSchedule schedule, DateOnly date, SchoolPeriod period) =>
        schedule.WithAdjustment(LessonAdjustment.ForPeriod(date, period, "Matematik"), a => a with { Hidden = true }, Monday).Normalized();

    [Fact]
    public void A_hidden_lesson_is_left_out_of_its_week_only_also_when_the_day_ends()
    {
        var sample = ScheduleLogicTests.Sample();
        var second = Lessons(sample)[1];
        var schedule = Hide(sample, Tuesday, second);

        var week = SchoolWeek.For(schedule, Monday);
        Assert.True(week.LessonAt(DayOfWeek.Tuesday, second).IsEmpty);
        Assert.Equal(new SchoolDaySpan(new(8, 0), new(8, 45)), week.DaySpan(DayOfWeek.Tuesday));
        Assert.Single(week.LessonsOn(DayOfWeek.Tuesday));
        Assert.Equal(SchoolWeek.For(sample, Monday).LessonCount - 1, week.LessonCount);
        Assert.Equal(Tuesday, Assert.Single(week.Hidden).Date);

        // The other days and the next week are untouched.
        Assert.Equal("Matematik", week.LessonAt(DayOfWeek.Wednesday, second).Subject?.Name);
        var next = SchoolWeek.For(schedule, Monday.AddDays(7));
        Assert.Equal("Matematik", next.LessonAt(DayOfWeek.Tuesday, second).Subject?.Name);
        Assert.Empty(next.Hidden);
    }

    [Fact]
    public void A_note_for_the_day_comes_after_the_timetables_own_note()
    {
        var sample = ScheduleLogicTests.Sample();
        var first = Lessons(sample)[0];
        var schedule = sample.WithAdjustment(LessonAdjustment.ForPeriod(Monday, first, "Dansk"), a => a with { Note = "Husk madpakke" }, Monday).Normalized();

        Assert.Equal("Lokale 64 · Husk madpakke", SchoolWeek.For(schedule, Monday).LessonAt(DayOfWeek.Monday, first).Note);
        Assert.Equal("Husk madpakke", SchoolWeek.For(schedule, Monday).TimetableLessonAt(DayOfWeek.Monday, first) is { } lesson
            ? schedule.FindAdjustment(lesson.Target)?.Note
            : null);
        Assert.Equal("Lokale 64", SchoolWeek.For(schedule, Monday.AddDays(7)).LessonAt(DayOfWeek.Monday, first).Note);
    }

    [Fact]
    public void Note_and_hidden_build_on_each_other_and_an_empty_adjustment_is_removed()
    {
        var sample = ScheduleLogicTests.Sample();
        var lesson = LessonAdjustment.ForPeriod(Tuesday, Lessons(sample)[1], "Matematik");

        var hidden = sample.WithAdjustment(lesson, a => a with { Hidden = true }, Monday);
        var noted = hidden.WithAdjustment(lesson, a => a with { Note = "Vikar" }, Monday);
        var adjustment = Assert.Single(noted.Adjustments);
        Assert.True(adjustment.Hidden);
        Assert.Equal("Vikar", adjustment.Note);

        var shown = noted.WithAdjustment(lesson, a => a with { Hidden = false }, Monday);
        Assert.Equal("Vikar", Assert.Single(shown.Adjustments).Note);
        Assert.Empty(shown.WithAdjustment(lesson, a => a with { Note = null }, Monday).Adjustments);
    }

    [Fact]
    public void Adjustments_from_past_weeks_are_tidied_away_on_the_next_change()
    {
        var sample = ScheduleLogicTests.Sample();
        var second = Lessons(sample)[1];
        var old = Hide(sample, Tuesday, second);

        var later = old.WithAdjustment(LessonAdjustment.ForPeriod(Tuesday.AddDays(7), second, "Matematik"), a => a with { Hidden = true }, Monday.AddDays(7));

        Assert.Equal(Tuesday.AddDays(7), Assert.Single(later.Adjustments).Date);
    }

    [Fact]
    public void Cleaning_up_drops_adjustments_for_removed_rows_but_keeps_calendar_lessons()
    {
        var sample = ScheduleLogicTests.Sample();
        var second = Lessons(sample)[1];
        var schedule = sample with
        {
            Adjustments =
            [
                LessonAdjustment.ForPeriod(Tuesday, second, "Matematik") with { Note = "  Vikar  " },
                LessonAdjustment.ForPeriod(Tuesday, second, "Matematik") with { Hidden = true },
                new LessonAdjustment { Date = Tuesday, PeriodId = Guid.NewGuid(), Hidden = true },
                new LessonAdjustment { Date = Tuesday, LessonKey = "1@moodle.aau.dk", Hidden = true },
                new LessonAdjustment { Date = Tuesday, LessonKey = "2@moodle.aau.dk", Note = " " },
            ],
        };

        var cleaned = schedule.Normalized().Adjustments;

        Assert.Equal(2, cleaned.Count);
        Assert.True(cleaned[0].Hidden); // the newest of the two for the same lesson
        Assert.Equal("1@moodle.aau.dk", cleaned[1].LessonKey);

        var withoutRow = (sample with { Adjustments = [LessonAdjustment.ForPeriod(Tuesday, second, "Matematik") with { Note = "  Vikar  " }] })
            .Normalized();
        Assert.Equal("Vikar", Assert.Single(withoutRow.Adjustments).Note);
        Assert.Empty((withoutRow with { Periods = [.. withoutRow.Periods.Where(p => p.Id != second.Id)] }).Normalized().Adjustments);
    }

    [Fact]
    public void A_hidden_calendar_lesson_is_left_out_and_a_note_follows_the_place()
    {
        var events = FeedTests.BusyAutumn();
        var exercises = events.Single(e => e.Key == "o00"); // Monday 5 October 08:15–10:00
        var lecture = events.Single(e => e.Key == "f00");
        var schedule = FeedTests.University()
            .WithAdjustment(LessonAdjustment.ForLesson(exercises), a => a with { Hidden = true }, Monday)
            .WithAdjustment(LessonAdjustment.ForLesson(lecture), a => a with { Note = "Tag computer med" }, Monday)
            .Normalized();

        var week = SchoolWeek.For(schedule, Monday, events);
        var rows = week.Schedule.Periods.Where(p => !p.IsBreak).ToList();

        Assert.True(week.LessonAt(DayOfWeek.Monday, rows[0]).IsEmpty);
        Assert.Equal("Tag computer med", week.LessonAt(DayOfWeek.Monday, rows[1]).Note);
        Assert.Equal(new SchoolDaySpan(new(10, 15), new(12, 0)), week.DaySpan(DayOfWeek.Monday));
        Assert.Equal(3, week.LessonCount);
        Assert.Equal("Øvelser", Assert.Single(week.Hidden).Title);

        var next = SchoolWeek.For(schedule, Monday.AddDays(7), events);
        Assert.Equal("Øvelser", next.LessonAt(DayOfWeek.Monday, rows[0]).Subject?.Name);
        Assert.Null(next.LessonAt(DayOfWeek.Monday, rows[1]).Note);
        Assert.Empty(next.Hidden);
    }

    [Fact]
    public void A_hidden_calendar_lesson_that_is_moved_stays_hidden_and_is_listed_in_its_new_week()
    {
        var events = FeedTests.BusyAutumn();
        var exercises = events.Single(e => e.Key == "o00");
        var schedule = FeedTests.University().WithAdjustment(LessonAdjustment.ForLesson(exercises), a => a with { Hidden = true }, Monday).Normalized();

        var moved = events.Select(e => e.Key == "o00" ? e with { Start = e.Start.AddDays(8), End = e.End.AddDays(8) } : e).ToList();

        Assert.Empty(SchoolWeek.For(schedule, Monday, moved).Hidden);
        var next = SchoolWeek.For(schedule, Monday.AddDays(7), moved);
        Assert.Equal(new DateOnly(2026, 10, 13), Assert.Single(next.Hidden).Date);
        Assert.DoesNotContain(next.LessonsOn(DayOfWeek.Tuesday), l => l.Start == new TimeOnly(8, 15));
    }

    [Fact]
    public async Task Adjustments_survive_a_restart()
    {
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var service = new SchoolScheduleService(paths, NullLogger<SchoolScheduleService>.Instance);
        var schedule = await service.AddAsync(ScheduleLogicTests.Sample());
        var second = Lessons(schedule)[1];
        await service.UpdateAsync(schedule.Id, s => s.WithAdjustment(LessonAdjustment.ForPeriod(Tuesday, second, "Matematik"), a => a with { Hidden = true, Note = "Vikar" }, Monday));

        var reloaded = new SchoolScheduleService(paths, NullLogger<SchoolScheduleService>.Instance).Find(schedule.Id)!;

        var adjustment = Assert.Single(reloaded.Adjustments);
        Assert.Equal((Tuesday, second.Id, true, "Vikar", "Matematik", second.Start, second.End),
            (adjustment.Date, adjustment.PeriodId!.Value, adjustment.Hidden, adjustment.Note, adjustment.Title, adjustment.Start, adjustment.End));
    }
}
