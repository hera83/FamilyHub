using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>What one cell shows in a given week.</summary>
/// <param name="Subject">This week's subject – null when the lesson is free this week.</param>
/// <param name="Note">"Lokale 64", "Husk idrætstøj".</param>
/// <param name="Alternates">The subject changes between odd and even weeks.</param>
/// <param name="NextWeekSubject">Next week's subject when it alternates – null if free.</param>
/// <param name="Time">From a calendar: the lesson's own time when it differs from the row ("13:00–14:00").</param>
/// <param name="More">From a calendar: further lessons in the same row ("+1 mere").</param>
/// <param name="Change">From a calendar: the lesson changed since it was last marked as seen – a cancelled one is shown struck through.</param>
public sealed record LessonView(
    SchoolSubject? Subject, string? Note, bool Alternates, SchoolSubject? NextWeekSubject, string? Time = null, int More = 0, FeedChangeKind? Change = null)
{
    public static LessonView Empty { get; } = new(null, null, false, null);

    public bool IsEmpty => Subject is null && Note is null && !Alternates;
}

/// <summary>The first and last lesson of a day.</summary>
public readonly record struct SchoolDaySpan(TimeOnly Start, TimeOnly End);

/// <summary>One lesson's time on a day – from the fixed timetable or the school's calendar.</summary>
public readonly record struct LessonTime(TimeOnly Start, TimeOnly End);

public enum SchoolDayState
{
    /// <summary>No lessons that day, or the last one is over – "Har fri".</summary>
    Free,

    /// <summary>The first lesson hasn't started yet – "Møder 08:15".</summary>
    BeforeFirst,

    /// <summary>In a lesson – "Fri 14:15".</summary>
    InLesson,

    /// <summary>Between two lessons – "Pause til 10:15".</summary>
    Break,
}

/// <param name="At">BeforeFirst: when the first lesson starts. Break: when the next one starts. InLesson: when the day ends.</param>
public readonly record struct SchoolDayStatus(SchoolDayState State, TimeOnly? At = null);

/// <summary>Which day the home screen card talks about.</summary>
public readonly record struct SchoolWidgetDay(DateOnly Date, bool IsToday);

/// <summary>The schedule's rules in one place – pure functions with tests.</summary>
public static class ScheduleRules
{
    /// <summary>The home screen card shows today's school from here …</summary>
    public static readonly TimeOnly MorningFrom = new(6, 0);

    /// <summary>… and tomorrow's from noon on a day without school …</summary>
    public static readonly TimeOnly TomorrowFrom = new(12, 0);

    /// <summary>… until the evening, when the kitchen is quiet.</summary>
    public static readonly TimeOnly EveningUntil = new(21, 0);

    public static bool IsSchoolDay(DayOfWeek day) => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>The week the page shows: this week on school days – next week on Saturday and Sunday.</summary>
    public static DateOnly DisplayedMonday(DateOnly today) =>
        IsSchoolDay(today.DayOfWeek) ? MondayOf(today) : MondayOf(today).AddDays(7);

    /// <summary>The Monday of the date's own week.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static int WeekOf(DateOnly date) => DanishFormat.WeekNumber(date);

    /// <summary>"ulige uge" / "lige uge".</summary>
    public static string ParityText(int isoWeek) => isoWeek % 2 == 0 ? "lige uge" : "ulige uge";

    /// <summary>True when at least one lesson in the schedules changes between odd and even weeks.</summary>
    public static bool UsesAlternatingWeeks(IEnumerable<SchoolSchedule> schedules) =>
        schedules.Any(s => s.Cells.Any(c => c.AlternatesWeekly));

    /// <summary>"Mandag".</summary>
    public static string DayName(DayOfWeek day) => DanishFormat.Capitalize(DanishFormat.Culture.DateTimeFormat.GetDayName(day));

    /// <summary>1 for the first lesson of the day, 2 for the next … – breaks are not counted.</summary>
    public static int LessonNumber(SchoolSchedule schedule, SchoolPeriod period)
    {
        var number = 0;
        foreach (var row in schedule.Periods)
        {
            if (!row.IsBreak)
            {
                number++;
            }

            if (row.Id == period.Id)
            {
                return row.IsBreak ? 0 : number;
            }
        }

        return 0;
    }

    /// <summary>"3. time" (folkeskole) or "3. modul" for a lesson, the label for a break ("Frikvarter").</summary>
    public static string PeriodTitle(SchoolSchedule schedule, SchoolPeriod period) =>
        period.IsBreak ? period.Label ?? "Pause" : $"{LessonNumber(schedule, period)}. {schedule.Level.LessonWord()}";

    public static string TimeText(SchoolPeriod period) => DanishFormat.TimeRange(period.Start, period.End);

    /// <summary>What the cell shows in the given week.</summary>
    public static LessonView LessonAt(SchoolSchedule schedule, DayOfWeek day, SchoolPeriod period, int isoWeek)
    {
        if (period.IsBreak || schedule.CellAt(day, period.Id) is not { } cell)
        {
            return new LessonView(null, null, false, null);
        }

        return new LessonView(
            schedule.FindSubject(cell.SubjectInWeek(isoWeek)),
            cell.Note,
            cell.AlternatesWeekly,
            cell.AlternatesWeekly ? schedule.FindSubject(cell.SubjectInWeek(isoWeek + 1)) : null);
    }

    /// <summary>From the first to the last lesson with a subject that week – null when there is no school that day.</summary>
    public static SchoolDaySpan? DaySpan(SchoolSchedule schedule, DayOfWeek day, int isoWeek)
    {
        if (!IsSchoolDay(day))
        {
            return null;
        }

        var lessons = schedule.Periods
            .Where(p => !p.IsBreak && schedule.CellAt(day, p.Id)?.SubjectInWeek(isoWeek) is not null)
            .ToList();
        return lessons.Count == 0 ? null : new SchoolDaySpan(lessons.Min(p => p.Start), lessons.Max(p => p.End));
    }

    /// <summary>"Fri 14:15" – or "Ingen skole" on a day without lessons.</summary>
    public static string EndText(SchoolDaySpan? span) => span is { } s ? $"Fri {DanishFormat.Time(s.End)}" : "Ingen skole";

    /// <summary>The lessons with a subject that day in the given week, in order.</summary>
    public static IReadOnlyList<LessonTime> Lessons(SchoolSchedule schedule, DayOfWeek day, int isoWeek) => IsSchoolDay(day)
        ? [.. schedule.Periods
            .Where(p => !p.IsBreak && schedule.CellAt(day, p.Id)?.SubjectInWeek(isoWeek) is not null)
            .Select(p => new LessonTime(p.Start, p.End))]
        : [];

    /// <summary>Where a child is in the school day at <paramref name="time"/> – for the home screen card.</summary>
    public static SchoolDayStatus StatusAt(IReadOnlyList<LessonTime> lessons, TimeOnly time)
    {
        if (lessons.Count == 0)
        {
            return new SchoolDayStatus(SchoolDayState.Free);
        }

        var first = lessons.Min(l => l.Start);
        var last = lessons.Max(l => l.End);
        if (time < first)
        {
            return new SchoolDayStatus(SchoolDayState.BeforeFirst, first);
        }

        if (time >= last)
        {
            return new SchoolDayStatus(SchoolDayState.Free);
        }

        return lessons.Any(l => time >= l.Start && time < l.End)
            ? new SchoolDayStatus(SchoolDayState.InLesson, last)
            : new SchoolDayStatus(SchoolDayState.Break, lessons.Where(l => l.Start > time).Min(l => l.Start));
    }

    /// <summary>"Har fri", "Møder 08:15", "Pause til 10:15", "Fri 14:15".</summary>
    public static string StatusText(SchoolDayStatus status) => status.State switch
    {
        SchoolDayState.BeforeFirst => $"Møder {DanishFormat.Time(status.At!.Value)}",
        SchoolDayState.Break => $"Pause til {DanishFormat.Time(status.At!.Value)}",
        SchoolDayState.InLesson => $"Fri {DanishFormat.Time(status.At!.Value)}",
        _ => "Har fri",
    };

    /// <summary>The lesson or break going on right now – null before, after and between rows.</summary>
    public static SchoolPeriod? CurrentPeriod(SchoolSchedule schedule, TimeOnly time) =>
        schedule.Periods.FirstOrDefault(p => time >= p.Start && time < p.End);

    /// <summary>Lessons with a subject in the given week – "28 timer om ugen".</summary>
    public static int LessonsPerWeek(SchoolSchedule schedule, int isoWeek) =>
        schedule.Cells.Count(c => c.SubjectInWeek(isoWeek) is not null);

    /// <summary>How many lessons a subject has in the given week.</summary>
    public static int LessonsOf(SchoolSchedule schedule, Guid subjectId, int isoWeek) =>
        schedule.Cells.Count(c => c.SubjectInWeek(isoWeek) == subjectId);

    /// <summary>
    /// The home screen card: today's school from 06:00 until the last child has finished, then tomorrow's
    /// until 21:00 (also from noon on a day without school, e.g. Sunday). Null when there is nothing to say.
    /// </summary>
    public static SchoolWidgetDay? WidgetDay(IReadOnlyList<SchoolSchedule> schedules, DateOnly today, TimeOnly time) =>
        WidgetDay(schedules, today, time, (schedule, date) => DaySpan(schedule, date.DayOfWeek, WeekOf(date)));

    /// <inheritdoc cref="WidgetDay(IReadOnlyList{SchoolSchedule}, DateOnly, TimeOnly)"/>
    /// <param name="spanOf">A schedule's day on a date – also for schedules fetched from a calendar (<see cref="SchoolWeek"/>).</param>
    public static SchoolWidgetDay? WidgetDay(IReadOnlyList<SchoolSchedule> schedules, DateOnly today, TimeOnly time, Func<SchoolSchedule, DateOnly, SchoolDaySpan?> spanOf)
    {
        if (schedules.Count == 0)
        {
            return null;
        }

        var todayEnd = LatestEnd(schedules, today, spanOf);
        if (todayEnd is { } end && time >= MorningFrom && time < end)
        {
            return new SchoolWidgetDay(today, IsToday: true);
        }

        var tomorrow = today.AddDays(1);
        var showTomorrow = time < EveningUntil && time >= (todayEnd ?? TomorrowFrom) && LatestEnd(schedules, tomorrow, spanOf) is not null;
        return showTomorrow ? new SchoolWidgetDay(tomorrow, IsToday: false) : null;
    }

    private static TimeOnly? LatestEnd(IReadOnlyList<SchoolSchedule> schedules, DateOnly date, Func<SchoolSchedule, DateOnly, SchoolDaySpan?> spanOf)
    {
        var ends = schedules.Select(s => spanOf(s, date)).OfType<SchoolDaySpan>().Select(s => s.End).ToList();
        return ends.Count == 0 ? null : ends.Max();
    }
}
