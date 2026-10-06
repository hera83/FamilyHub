using FamilyHub.Core.Household;
using FamilyHub.Modules.School.Schedules;

namespace FamilyHub.Tests.School;

public sealed class ScheduleLogicTests
{
    /// <summary>A test schedule on the standard day: Dansk in the 1st lesson every day, Billedkunst/Tysk alternating on Friday's 3rd.</summary>
    internal static SchoolSchedule Sample(Guid? memberId = null)
    {
        var periods = PeriodPlanner.StandardDay();
        var lessons = periods.Where(p => !p.IsBreak).ToList();
        var dansk = new SchoolSubject { Name = "Dansk", Color = MemberColor.Terracotta };
        var mat = new SchoolSubject { Name = "Matematik", Color = MemberColor.Sky };
        var bil = new SchoolSubject { Name = "Billedkunst", Color = MemberColor.Rose };
        var tys = new SchoolSubject { Name = "Tysk", Color = MemberColor.Sand };

        var cells = new List<ScheduleCell>();
        foreach (var day in SchoolSchedule.SchoolDays)
        {
            cells.Add(new ScheduleCell { Day = day, PeriodId = lessons[0].Id, SubjectId = dansk.Id, Note = day == DayOfWeek.Monday ? "Lokale 64" : null });
            cells.Add(new ScheduleCell { Day = day, PeriodId = lessons[1].Id, SubjectId = mat.Id });
        }

        // Monday has all seven lessons (until 14:15), Wednesday only two (until 09:30).
        foreach (var lesson in lessons.Skip(2))
        {
            cells.Add(new ScheduleCell { Day = DayOfWeek.Monday, PeriodId = lesson.Id, SubjectId = mat.Id });
        }

        cells.Add(new ScheduleCell { Day = DayOfWeek.Friday, PeriodId = lessons[2].Id, SubjectId = bil.Id, AlternatesWeekly = true, EvenWeekSubjectId = tys.Id });

        return new SchoolSchedule
        {
            MemberId = memberId ?? Guid.NewGuid(),
            Grade = 6,
            ClassLetter = "A",
            Subjects = [dansk, mat, bil, tys],
            Periods = periods,
            Cells = cells,
        }.Normalized();
    }

    [Theory]
    [InlineData(SchoolLevel.PrimarySchool, 6, "A", "6.a")]
    [InlineData(SchoolLevel.PrimarySchool, 0, "b", "0.b")]
    [InlineData(SchoolLevel.PrimarySchool, 3, null, "3. klasse")]
    [InlineData(SchoolLevel.Gymnasium, 2, null, "2.g")]
    [InlineData(SchoolLevel.Gymnasium, 2, "B", "2.b")]
    [InlineData(SchoolLevel.University, 3, "A", "3. semester")]
    public void Class_names_are_written_the_Danish_way(SchoolLevel level, int grade, string? letter, string expected) =>
        Assert.Equal(expected, level.ClassName(grade, letter));

    [Theory]
    [InlineData(SchoolLevel.PrimarySchool, 0, 10, "Klassetrin", "0. klasse (bh.)")]
    [InlineData(SchoolLevel.Gymnasium, 1, 3, "Årgang", "1.g")]
    [InlineData(SchoolLevel.University, 1, 12, "Semester", "1. semester")]
    public void Each_kind_of_school_has_its_own_grade_steps(SchoolLevel level, int min, int max, string label, string firstText)
    {
        Assert.Equal(min, level.MinGrade());
        Assert.Equal(max, level.MaxGrade());
        Assert.Equal(label, level.GradeLabel());
        Assert.Equal(firstText, level.GradeText(min));
    }

    [Fact]
    public void Gymnasium_and_university_have_modules_instead_of_lessons()
    {
        var schedule = Sample() with { Level = SchoolLevel.Gymnasium };

        Assert.Equal("1. modul", ScheduleRules.PeriodTitle(schedule, schedule.Periods[0]));
        Assert.Equal("8 moduler", SchoolLevel.University.LessonCount(8));
        Assert.Equal("1 time", SchoolLevel.PrimarySchool.LessonCount(1));
    }

    [Theory]
    [InlineData(SchoolLevel.PrimarySchool, 7, "14:15")]
    [InlineData(SchoolLevel.Gymnasium, 4, "15:15")]
    [InlineData(SchoolLevel.University, 4, "16:30")]
    public void Every_kind_of_school_starts_with_a_sensible_day(SchoolLevel level, int lessons, string end)
    {
        var day = PeriodPlanner.StandardDay(level);

        Assert.Equal(lessons, day.Count(p => !p.IsBreak));
        Assert.Equal(TimeOnly.Parse(end), day[^1].End);
        Assert.All(day.Zip(day.Skip(1)), pair => Assert.Equal(pair.First.End, pair.Second.Start));
    }

    [Theory]
    [InlineData("2026-10-06", "2026-10-05")] // Tuesday → this week
    [InlineData("2026-10-09", "2026-10-05")] // Friday → this week
    [InlineData("2026-10-10", "2026-10-12")] // Saturday → next week
    [InlineData("2026-10-11", "2026-10-12")] // Sunday → next week
    public void At_the_weekend_the_page_shows_next_week(string today, string monday) =>
        Assert.Equal(DateOnly.Parse(monday), ScheduleRules.DisplayedMonday(DateOnly.Parse(today)));

    [Theory]
    [InlineData("2026-10-05", "2026-10-06", "Uge 41", "i denne uge")]
    [InlineData("2026-10-12", "2026-10-06", "Næste uge · uge 42", "i næste uge")]
    [InlineData("2026-10-12", "2026-10-11", "Næste uge · uge 42", "i næste uge")] // Sunday: Monday is next week
    [InlineData("2026-10-26", "2026-10-06", "Om 3 uger · uge 44", "i uge 44")]
    [InlineData("2026-09-28", "2026-10-06", "Sidste uge · uge 40", "i uge 40")]
    [InlineData("2026-12-28", "2026-12-22", "Næste uge · uge 53", "i næste uge")] // across New Year
    public void The_week_shown_is_named_from_today(string monday, string today, string text, string inWeek)
    {
        Assert.Equal(text, ScheduleRules.WeekText(DateOnly.Parse(monday), DateOnly.Parse(today)));
        Assert.Equal(inWeek, ScheduleRules.InWeekText(DateOnly.Parse(monday), DateOnly.Parse(today)));
    }

    [Fact]
    public void The_day_ends_with_the_last_lesson_that_has_a_subject()
    {
        var schedule = Sample();

        Assert.Equal(new SchoolDaySpan(new(8, 0), new(14, 15)), ScheduleRules.DaySpan(schedule, DayOfWeek.Monday, 41));
        Assert.Equal(new SchoolDaySpan(new(8, 0), new(9, 30)), ScheduleRules.DaySpan(schedule, DayOfWeek.Wednesday, 41));
        Assert.Null(ScheduleRules.DaySpan(schedule, DayOfWeek.Saturday, 41));
        Assert.Equal("Fri 09:30", ScheduleRules.EndText(ScheduleRules.DaySpan(schedule, DayOfWeek.Wednesday, 41)));
        Assert.Equal("Ingen skole", ScheduleRules.EndText(null));
    }

    [Fact]
    public void An_alternating_lesson_shows_this_weeks_subject_and_next_weeks()
    {
        var schedule = Sample();
        var third = schedule.Periods.Where(p => !p.IsBreak).ElementAt(2);

        var odd = ScheduleRules.LessonAt(schedule, DayOfWeek.Friday, third, 41);
        var even = ScheduleRules.LessonAt(schedule, DayOfWeek.Friday, third, 42);

        Assert.Equal("Billedkunst", odd.Subject?.Name);
        Assert.Equal("Tysk", odd.NextWeekSubject?.Name);
        Assert.Equal("Tysk", even.Subject?.Name);
        Assert.Equal("Billedkunst", even.NextWeekSubject?.Name);
        Assert.True(ScheduleRules.UsesAlternatingWeeks([schedule]));
    }

    [Fact]
    public void Lessons_are_numbered_without_the_breaks()
    {
        var schedule = Sample();
        var titles = schedule.Periods.Select(p => ScheduleRules.PeriodTitle(schedule, p)).ToList();

        Assert.Equal(["1. time", "2. time", "Frikvarter", "3. time", "4. time", "Spisepause", "5. time", "6. time", "Frikvarter", "7. time"], titles);
    }

    [Theory]
    [InlineData("08:00", "1. time")]
    [InlineData("08:44", "1. time")]
    [InlineData("08:45", "2. time")]
    [InlineData("09:35", "Frikvarter")]
    [InlineData("07:59", null)]
    [InlineData("14:15", null)]
    public void The_current_period_starts_inclusive_and_ends_exclusive(string time, string? expected)
    {
        var schedule = Sample();

        var current = ScheduleRules.CurrentPeriod(schedule, TimeOnly.Parse(time));

        Assert.Equal(expected, current is null ? null : ScheduleRules.PeriodTitle(schedule, current));
    }

    [Theory]
    [InlineData("2026-10-05", "05:59", null)]                 // Monday, too early
    [InlineData("2026-10-05", "06:00", "2026-10-05")]         // Monday morning → today
    [InlineData("2026-10-05", "14:14", "2026-10-05")]         // still at school
    [InlineData("2026-10-05", "14:15", "2026-10-06")]         // finished → tomorrow
    [InlineData("2026-10-07", "09:30", "2026-10-08")]         // Wednesday ends early → Thursday from 09:30
    [InlineData("2026-10-05", "21:00", null)]                 // evening – quiet
    [InlineData("2026-10-09", "15:00", null)]                 // Friday afternoon – no school tomorrow
    [InlineData("2026-10-11", "11:59", null)]                 // Sunday morning
    [InlineData("2026-10-11", "12:00", "2026-10-12")]         // Sunday from noon → Monday
    public void The_home_card_talks_about_today_then_tomorrow(string date, string time, string? expected)
    {
        var result = ScheduleRules.WidgetDay([Sample()], DateOnly.Parse(date), TimeOnly.Parse(time));

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), result?.Date);
        if (result is { } day)
        {
            Assert.Equal(day.Date == DateOnly.Parse(date), day.IsToday);
        }
    }

    [Theory]
    [InlineData("07:30", "Møder 08:00", SchoolDayState.BeforeFirst)]
    [InlineData("08:00", "Fri 12:00", SchoolDayState.InLesson)]
    [InlineData("09:00", "Pause til 09:15", SchoolDayState.Break)]   // a short break
    [InlineData("10:30", "Pause til 11:15", SchoolDayState.Break)]   // a free period in the middle of the day
    [InlineData("11:59", "Fri 12:00", SchoolDayState.InLesson)]
    [InlineData("12:00", "Har fri", SchoolDayState.Free)]
    [InlineData("16:00", "Har fri", SchoolDayState.Free)]
    public void The_school_day_status_follows_the_lessons(string time, string text, SchoolDayState state)
    {
        IReadOnlyList<LessonTime> lessons =
        [
            new(new(8, 0), new(9, 0)),
            new(new(9, 15), new(10, 0)),
            new(new(11, 15), new(12, 0)),
        ];

        var status = ScheduleRules.StatusAt(lessons, TimeOnly.Parse(time));

        Assert.Equal(state, status.State);
        Assert.Equal(text, ScheduleRules.StatusText(status));
    }

    [Fact]
    public void A_day_without_lessons_is_free_all_day()
    {
        Assert.Equal("Har fri", ScheduleRules.StatusText(ScheduleRules.StatusAt([], new TimeOnly(7, 0))));

        // Wednesday in the sample has two lessons, 08:00–09:30.
        Assert.Equal([new LessonTime(new(8, 0), new(8, 45)), new LessonTime(new(8, 45), new(9, 30))], ScheduleRules.Lessons(Sample(), DayOfWeek.Wednesday, 41));
        Assert.Empty(ScheduleRules.Lessons(Sample(), DayOfWeek.Saturday, 41));
    }

    [Fact]
    public void Without_schedules_the_home_card_is_not_shown() =>
        Assert.Null(ScheduleRules.WidgetDay([], new DateOnly(2026, 10, 5), new TimeOnly(9, 0)));

    [Fact]
    public void Lessons_per_week_count_only_this_weeks_subjects()
    {
        var schedule = Sample();
        var bil = schedule.Subjects.Single(s => s.Name == "Billedkunst").Id;

        Assert.Equal(1, ScheduleRules.LessonsOf(schedule, bil, 41));
        Assert.Equal(0, ScheduleRules.LessonsOf(schedule, bil, 42));
        Assert.Equal(7 + 2 + 2 + 2 + 3, ScheduleRules.LessonsPerWeek(schedule, 41)); // Monday … Friday (Billedkunst in odd weeks)
        Assert.Equal(7 + 2 + 2 + 2 + 3, ScheduleRules.LessonsPerWeek(schedule, 42)); // Tysk in even weeks
    }

    [Fact]
    public void Changing_a_lesson_moves_the_rows_after_it_along()
    {
        var periods = PeriodPlanner.StandardDay();
        var first = periods[0];

        // 1st lesson 08:00–08:45 becomes 08:00–08:50: everything after moves 5 minutes.
        var moved = PeriodPlanner.Change(periods, first.Id, 8 * 60, 50, null, moveFollowing: true);

        Assert.Equal(new TimeOnly(8, 50), moved[0].End);
        Assert.Equal(new TimeOnly(8, 50), moved[1].Start);
        Assert.Equal(new TimeOnly(14, 20), moved[^1].End);

        var alone = PeriodPlanner.Change(periods, first.Id, 8 * 60, 50, null, moveFollowing: false);
        Assert.Equal(new TimeOnly(8, 45), alone[1].Start);
    }

    [Fact]
    public void A_new_break_goes_in_after_the_chosen_row_and_pushes_the_rest()
    {
        var periods = PeriodPlanner.StandardDay().Where(p => !p.IsBreak).ToList();
        var second = periods[1];

        var result = PeriodPlanner.InsertBreak(periods, second.Id, "Formiddagsmad", 20, moveFollowing: true);

        Assert.Equal(PeriodKind.Break, result[2].Kind);
        Assert.Equal("Formiddagsmad", result[2].Label);
        Assert.Equal(new TimeOnly(9, 30), result[2].Start);
        Assert.Equal(new TimeOnly(9, 50), result[2].End);
        Assert.Equal(new TimeOnly(10, 10), result[3].Start); // the 3rd lesson (09:50) moves 20 minutes
        Assert.Equal(periods.Count + 1, result.Count);

        var kept = PeriodPlanner.InsertBreak(periods, second.Id, "Pause", 20, moveFollowing: false);
        Assert.Equal(new TimeOnly(9, 50), kept[3].Start);
    }

    [Fact]
    public void A_new_lesson_follows_the_last_row_with_the_same_length()
    {
        IReadOnlyList<SchoolPeriod> periods =
        [
            new() { Kind = PeriodKind.Lesson, Start = new(8, 0), End = new(8, 45) },
            new() { Kind = PeriodKind.Lesson, Start = new(8, 45), End = new(9, 10) },
        ];

        var result = PeriodPlanner.AddLesson(periods);

        Assert.Equal(new TimeOnly(9, 10), result[^1].Start);
        Assert.Equal(new TimeOnly(9, 35), result[^1].End);
        Assert.Equal(new TimeOnly(8, 0), PeriodPlanner.AddLesson([])[0].Start);
    }

    [Fact]
    public void Times_never_wrap_past_midnight()
    {
        var late = new SchoolPeriod { Kind = PeriodKind.Lesson, Start = new(23, 0), End = new(23, 50) };

        var result = PeriodPlanner.InsertBreak([late], null, "Pause", 20, moveFollowing: true);

        Assert.All(result, p => Assert.True(p.End > p.Start));
        Assert.Equal(new TimeOnly(23, 55), result.Max(p => p.End));
    }

    [Fact]
    public void Normalizing_drops_cells_whose_subject_or_row_is_gone()
    {
        var schedule = Sample();
        var dansk = schedule.Subjects.Single(s => s.Name == "Dansk").Id;
        var lastLesson = schedule.Periods.Last(p => !p.IsBreak).Id;

        var cleaned = (schedule with
        {
            Subjects = [.. schedule.Subjects.Where(s => s.Id != dansk)],
            Periods = [.. schedule.Periods.Where(p => p.Id != lastLesson)],
        }).Normalized();

        Assert.DoesNotContain(cleaned.Cells, c => c.SubjectId == dansk);
        Assert.DoesNotContain(cleaned.Cells, c => c.PeriodId == lastLesson);

        // Monday's first lesson keeps its note, so the cell stays – without a subject.
        Assert.Contains(cleaned.Cells, c => c.Day == DayOfWeek.Monday && c.SubjectId is null && c.Note == "Lokale 64");
    }

    [Fact]
    public void Normalizing_cleans_up_a_hand_edited_file()
    {
        var subject = new SchoolSubject { Name = "  Dansk  " };
        var period = new SchoolPeriod { Kind = PeriodKind.Lesson, Start = new(8, 0), End = new(8, 45) };
        var schedule = new SchoolSchedule
        {
            Grade = 14,
            ClassLetter = "q",
            Subjects = [subject, new SchoolSubject { Name = "dansk" }, new SchoolSubject { Name = " " }],
            Periods = [period, new SchoolPeriod { Start = new(9, 0), End = new(9, 0) }, new SchoolPeriod { Kind = PeriodKind.Break, Start = new(7, 0), End = new(7, 10) }],
            Cells =
            [
                new ScheduleCell { Day = DayOfWeek.Saturday, PeriodId = period.Id, SubjectId = subject.Id },
                new ScheduleCell { Day = DayOfWeek.Monday, PeriodId = period.Id, SubjectId = subject.Id, AlternatesWeekly = true, EvenWeekSubjectId = subject.Id },
            ],
        }.Normalized();

        Assert.Equal(SchoolLevel.PrimarySchool.MaxGrade(), schedule.Grade);
        Assert.Null(schedule.ClassLetter);
        Assert.Equal("Dansk", Assert.Single(schedule.Subjects).Name);
        Assert.Equal(2, schedule.Periods.Count);
        Assert.Equal("Pause", schedule.Periods[0].Label);
        var cell = Assert.Single(schedule.Cells);
        Assert.False(cell.AlternatesWeekly);
        Assert.Null(cell.EvenWeekSubjectId);
    }

    [Fact]
    public void Normalizing_keeps_the_grade_and_letter_within_the_kind_of_school()
    {
        var gymnasium = new SchoolSchedule { Level = SchoolLevel.Gymnasium, Grade = 6, ClassLetter = "b" }.Normalized();
        var university = new SchoolSchedule { Level = SchoolLevel.University, Grade = 0, ClassLetter = "A" }.Normalized();
        var unknown = new SchoolSchedule { Level = (SchoolLevel)42, Grade = 4 }.Normalized();

        Assert.Equal("3.b", gymnasium.ClassName);
        Assert.Equal(1, university.Grade);
        Assert.Null(university.ClassLetter);
        Assert.Equal(SchoolLevel.PrimarySchool, unknown.Level);
    }

    [Fact]
    public void An_emptied_cell_is_removed()
    {
        var schedule = Sample();
        var first = schedule.Periods[0].Id;

        var cleared = schedule.WithCell(new ScheduleCell { Day = DayOfWeek.Tuesday, PeriodId = first });

        Assert.Null(cleared.CellAt(DayOfWeek.Tuesday, first));
        Assert.Equal(schedule.Cells.Count - 1, cleared.Cells.Count);
    }

    [Fact]
    public void The_suggested_subjects_follow_the_grade()
    {
        var zero = SubjectCatalog.For(SchoolLevel.PrimarySchool, 0).Select(s => s.Name).ToList();
        var sixth = SubjectCatalog.For(SchoolLevel.PrimarySchool, 6).Select(s => s.Name).ToList();
        var eighth = SubjectCatalog.For(SchoolLevel.PrimarySchool, 8).Select(s => s.Name).ToList();
        var firstG = SubjectCatalog.For(SchoolLevel.Gymnasium, 1).Select(s => s.Name).ToList();
        var thirdG = SubjectCatalog.For(SchoolLevel.Gymnasium, 3).Select(s => s.Name).ToList();

        Assert.Contains("Bh. klasse", zero);
        Assert.DoesNotContain("Dansk", zero);
        Assert.Contains("Tysk", sixth);
        Assert.Contains("Natur/teknologi", sixth);
        Assert.DoesNotContain("Fysik/kemi", sixth);
        Assert.Contains("Fysik/kemi", eighth);
        Assert.Contains("Samfundsfag", eighth);
        Assert.Contains("Naturgeografi", firstG);
        Assert.DoesNotContain("Religion", firstG);
        Assert.Contains("Religion", thirdG);
        Assert.Contains("Kemi", thirdG);

        // A university student adds their own courses.
        Assert.Empty(SubjectCatalog.For(SchoolLevel.University, 3));

        // The big subjects look the same in every child's schedule.
        Assert.Equal(MemberColor.Terracotta, SubjectCatalog.For(SchoolLevel.PrimarySchool, 2).Single(s => s.Name == "Dansk").Color);
        Assert.Equal(MemberColor.Terracotta, SubjectCatalog.For(SchoolLevel.Gymnasium, 2).Single(s => s.Name == "Dansk").Color);
    }

    [Fact]
    public void A_new_subject_gets_the_least_used_colour()
    {
        var subjects = Enum.GetValues<MemberColor>().Where(c => c != MemberColor.Plum).Select(c => new SchoolSubject { Name = c.ToString(), Color = c });

        Assert.Equal(MemberColor.Plum, SubjectCatalog.NextColor(subjects));
    }
}
