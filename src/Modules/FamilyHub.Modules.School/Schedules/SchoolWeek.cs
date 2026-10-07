using FamilyHub.Core.Household;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>A lesson from the school's calendar as the details dialog shows it – with its change, if not yet marked as seen.</summary>
public sealed record CalendarLesson(FeedEvent Lesson, FeedChange? Change)
{
    /// <summary>What "Skjul" and the note are saved on.</summary>
    public LessonAdjustment Target => LessonAdjustment.ForLesson(Lesson);
}

/// <summary>A lesson in the fixed timetable on one date, as the details dialog shows it.</summary>
/// <param name="Target">The lesson on its date – its title is the subject.</param>
/// <param name="TimetableNote">The note set in the settings ("Lokale 64") – the same every week.</param>
public sealed record TimetableLesson(LessonAdjustment Target, string? TimetableNote);

/// <summary>
/// What one schedule shows in one week – the fixed timetable, or this week's lessons from the school's calendar.
/// With a link everything is bound to the calendar: also the rows, made from its recurring lesson times on every fetch
/// (the fixed timetable's own rows are kept untouched for when the link is removed). The table, the tabs and the home card
/// ask this, so they need not know the difference.
/// Lessons the family has hidden that week are left out everywhere – also of "Fri 14:15" and the home card – and a note
/// for the day is shown under the lesson (<see cref="SchoolSchedule.Adjustments"/>).
/// </summary>
public sealed class SchoolWeek
{
    private readonly IReadOnlyList<FeedEvent> events;

    private SchoolWeek(SchoolSchedule schedule, DateOnly monday, IReadOnlyList<FeedEvent> events, IReadOnlyList<FeedChange> changes, IReadOnlyList<LessonAdjustment> hidden)
    {
        Schedule = schedule;
        Monday = monday;
        IsoWeek = ScheduleRules.WeekOf(monday);
        this.events = events;
        Changes = changes;
        Hidden = hidden;
    }

    /// <summary>The lessons hidden this week, in order – for "Skjulte timer", where they can be shown again.</summary>
    public IReadOnlyList<LessonAdjustment> Hidden { get; }

    /// <summary>Changes in the calendar this week that nobody has marked as seen yet.</summary>
    public IReadOnlyList<FeedChange> Changes { get; }

    public SchoolSchedule Schedule { get; }

    public DateOnly Monday { get; }

    public int IsoWeek { get; }

    /// <summary>True when the lessons come from the school's calendar.</summary>
    public bool IsLive => Schedule.UsesFeed;

    /// <param name="feedEvents">The schedule's calendar (all weeks) – ignored when the schedule has no link.</param>
    /// <param name="changes">Changes not yet marked as seen (<see cref="ScheduleFeedService.ChangesFor"/>).</param>
    public static SchoolWeek For(SchoolSchedule schedule, DateOnly monday, IReadOnlyList<FeedEvent>? feedEvents = null, IReadOnlyList<FeedChange>? changes = null)
    {
        var sunday = monday.AddDays(6);
        if (!schedule.UsesFeed || feedEvents is null)
        {
            var rows = schedule.Periods.Select(p => p.Id).ToHashSet();
            return new SchoolWeek(schedule, monday, [], [], Ordered(schedule.Adjustments.Where(a =>
                a.Hidden && a.PeriodId is { } row && rows.Contains(row) && a.Date >= monday && a.Date <= sunday)));
        }

        // The rows come from the whole calendar (not just this week), so they stay the same from week to week.
        // An empty calendar (e.g. the holidays) keeps the schedule's own rows.
        var effective = PeriodPlanner.FromEvents(feedEvents) is { } periods ? schedule with { Periods = periods } : schedule;

        // A hidden lesson that has been moved is hidden where it is now – and listed in that week.
        var hidden = schedule.Adjustments
            .Where(a => a.Hidden && a.LessonKey is not null)
            .Select(a => feedEvents.FirstOrDefault(e => FeedChanges.KeyOf(e) == a.LessonKey) is { } now
                ? a with { Date = DateOnly.FromDateTime(now.Start), Title = now.Title, Start = TimeOnly.FromDateTime(now.Start), End = TimeOnly.FromDateTime(now.End) }
                : a)
            .ToList();
        var hiddenKeys = hidden.Select(a => a.LessonKey!).ToHashSet();

        var from = monday.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        return new SchoolWeek(
            effective,
            monday,
            [.. feedEvents.Where(e => e.Start < to && e.End > from && !hiddenKeys.Contains(FeedChanges.KeyOf(e))).OrderBy(e => e.Start)],
            [.. (changes ?? []).Where(c => c.Date >= monday && c.Date <= sunday && !hiddenKeys.Contains(FeedChanges.KeyOf(c.Shown)))],
            Ordered(hidden.Where(a => a.Date >= monday && a.Date <= sunday)));
    }

    private static IReadOnlyList<LessonAdjustment> Ordered(IEnumerable<LessonAdjustment> adjustments) =>
        [.. adjustments.OrderBy(a => a.Date).ThenBy(a => a.Start)];

    public DateOnly DateOf(DayOfWeek day) => Monday.AddDays(((int)day + 6) % 7);

    /// <summary>What the cell shows: the subject (or the first lesson from the calendar, with its place and own time).</summary>
    public LessonView LessonAt(DayOfWeek day, SchoolPeriod period)
    {
        if (!IsLive)
        {
            var planned = ScheduleRules.LessonAt(Schedule, day, period, IsoWeek);
            return period.IsBreak || AdjustmentAt(day, period) is not { } adjustment
                ? planned
                : adjustment.Hidden ? LessonView.Empty : planned with { Note = Join(planned.Note, adjustment.Note) };
        }

        if (period.IsBreak)
        {
            return LessonView.Empty;
        }

        var date = DateOf(day);
        var inRow = EventsIn(date, period);
        if (inRow.Count == 0)
        {
            // A cancelled lesson stays in its row, struck through, until it has been marked as seen.
            return Changes.FirstOrDefault(c => c.Kind == FeedChangeKind.Cancelled && Overlaps(c.Before!, date, period)) is { } cancelled
                ? new LessonView(SubjectFor(cancelled.Before!.Title), Join(cancelled.Before!.Location, NoteOf(cancelled.Before!)), false, null, OwnTime(cancelled.Before!, period), 0, FeedChangeKind.Cancelled)
                : LessonView.Empty;
        }

        var first = inRow[0];
        return new LessonView(SubjectFor(first.Title), Join(first.Location, NoteOf(first)), false, null, OwnTime(first, period), inRow.Count - 1, ChangeOf(first)?.Kind);
    }

    /// <summary>A lesson in the fixed timetable, for the details dialog – null for a break, an empty cell or with a calendar.</summary>
    public TimetableLesson? TimetableLessonAt(DayOfWeek day, SchoolPeriod period)
    {
        if (IsLive || period.IsBreak || LessonAt(day, period) is not { IsEmpty: false } lesson)
        {
            return null;
        }

        var title = lesson.Subject?.Name ?? (lesson.Alternates ? "Fri denne uge" : ScheduleRules.PeriodTitle(Schedule, period));
        return new TimetableLesson(LessonAdjustment.ForPeriod(DateOf(day), period, title), Schedule.CellAt(day, period.Id)?.Note);
    }

    /// <summary>The family's note for a calendar lesson that day – null when there is none.</summary>
    public string? NoteOf(FeedEvent lesson) => Schedule.FindAdjustment(LessonAdjustment.ForLesson(lesson))?.Note;

    private LessonAdjustment? AdjustmentAt(DayOfWeek day, SchoolPeriod period) =>
        Schedule.Adjustments.Count == 0 ? null : Schedule.FindAdjustment(LessonAdjustment.ForPeriod(DateOf(day), period, ""));

    /// <summary>The fixed timetable's lessons with a subject that day, without the hidden ones.</summary>
    private IEnumerable<SchoolPeriod> TimetableLessons(DayOfWeek day) => ScheduleRules.IsSchoolDay(day)
        ? Schedule.Periods.Where(p => !p.IsBreak && Schedule.CellAt(day, p.Id)?.SubjectInWeek(IsoWeek) is not null && AdjustmentAt(day, p)?.Hidden != true)
        : [];

    /// <summary>"Lokale 64 · Husk madpakke" – either alone when the other is missing.</summary>
    private static string? Join(string? first, string? second) => (first, second) switch
    {
        (null, _) => second,
        (_, null) => first,
        _ => $"{first} · {second}",
    };

    /// <summary>Every calendar lesson in the cell, for the details dialog – or the cancelled one still shown there.</summary>
    public IReadOnlyList<CalendarLesson> CalendarLessonsAt(DayOfWeek day, SchoolPeriod period)
    {
        if (!IsLive || period.IsBreak)
        {
            return [];
        }

        var date = DateOf(day);
        var inRow = EventsIn(date, period);
        if (inRow.Count > 0)
        {
            return [.. inRow.Select(Details)];
        }

        return Changes.FirstOrDefault(c => c.Kind == FeedChangeKind.Cancelled && Overlaps(c.Before!, date, period)) is { } cancelled
            ? [new CalendarLesson(cancelled.Before!, cancelled)]
            : [];
    }

    /// <summary>One calendar lesson with its change – also for the lessons under the table.</summary>
    public CalendarLesson Details(FeedEvent lesson) => new(lesson, ChangeOf(lesson));

    private FeedChange? ChangeOf(FeedEvent lesson) =>
        Changes.FirstOrDefault(c => c.After is { } after && FeedChanges.KeyOf(after) == FeedChanges.KeyOf(lesson) && after.Start == lesson.Start);

    /// <summary>The lesson's own time when it differs from the row ("10:15–12:00").</summary>
    private static string? OwnTime(FeedEvent lesson, SchoolPeriod period) =>
        TimeOnly.FromDateTime(lesson.Start) != period.Start || TimeOnly.FromDateTime(lesson.End) != period.End
            ? DanishFormat.TimeRange(TimeOnly.FromDateTime(lesson.Start), TimeOnly.FromDateTime(lesson.End))
            : null;

    /// <summary>From the first to the last lesson that day – null when there is none.</summary>
    public SchoolDaySpan? DaySpan(DayOfWeek day)
    {
        if (!IsLive)
        {
            var lessons = TimetableLessons(day).ToList();
            return lessons.Count == 0 ? null : new SchoolDaySpan(lessons.Min(p => p.Start), lessons.Max(p => p.End));
        }

        if (!ScheduleRules.IsSchoolDay(day))
        {
            return null;
        }

        var date = DateOf(day);
        var onDay = events.Where(e => DateOnly.FromDateTime(e.Start) == date).ToList();
        return onDay.Count == 0
            ? null
            : new SchoolDaySpan(TimeOnly.FromDateTime(onDay.Min(e => e.Start)), TimeOnly.FromDateTime(onDay.Max(e => e.End)));
    }

    /// <summary>That day's lessons in order – the rows with a subject, or every lesson in the calendar that day.</summary>
    public IReadOnlyList<LessonTime> LessonsOn(DayOfWeek day)
    {
        if (!IsLive)
        {
            return [.. TimetableLessons(day).Select(p => new LessonTime(p.Start, p.End))];
        }

        var date = DateOf(day);
        return ScheduleRules.IsSchoolDay(day)
            ? [.. events.Where(e => DateOnly.FromDateTime(e.Start) == date)
                .Select(e => new LessonTime(TimeOnly.FromDateTime(e.Start), TimeOnly.FromDateTime(e.End)))
                .OrderBy(l => l.Start)]
            : [];
    }

    /// <summary>Lessons from the calendar on a school day that fit in none of the rows – listed under the table.</summary>
    public IReadOnlyList<FeedEvent> Outside => IsLive
        ? [.. events.Where(e => ScheduleRules.IsSchoolDay(e.Start.DayOfWeek)
                                && !Schedule.Periods.Any(p => !p.IsBreak && Overlaps(e, DateOnly.FromDateTime(e.Start), p)))]
        : [];

    /// <summary>Lessons that week – "8 moduler om ugen".</summary>
    public int LessonCount => IsLive
        ? events.Count(e => ScheduleRules.IsSchoolDay(e.Start.DayOfWeek))
        : SchoolSchedule.SchoolDays.Sum(day => TimetableLessons(day).Count());

    /// <summary>
    /// A calendar title gets the colour of a subject with the same name ("Statistik – forelæsning 3" → Statistik).
    /// Otherwise a colour of its own, the same every week: the part before " - ", "(" or ":" decides it.
    /// </summary>
    internal SchoolSubject SubjectFor(string title)
    {
        if (Schedule.Subjects.FirstOrDefault(s => title.StartsWith(s.Name, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            return known with { Name = title };
        }

        var key = CourseKey(title);
        var palette = Enum.GetValues<MemberColor>();
        return new SchoolSubject { Name = title, Color = palette[(int)(StableHash(key) % (uint)palette.Length)] };
    }

    internal static string CourseKey(string title)
    {
        var end = title.Length;
        foreach (var separator in new[] { " - ", " – ", "(", ":" })
        {
            var at = title.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0 && at < end)
            {
                end = at;
            }
        }

        return title[..end].Trim().ToLowerInvariant();
    }

    /// <summary>FNV-1a – unlike string.GetHashCode it is the same in every run, so a course keeps its colour.</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    private List<FeedEvent> EventsIn(DateOnly date, SchoolPeriod period) => [.. events.Where(e => Overlaps(e, date, period))];

    private static bool Overlaps(FeedEvent e, DateOnly date, SchoolPeriod period)
    {
        var rowStart = date.ToDateTime(period.Start);
        var rowEnd = date.ToDateTime(period.End);
        return e.Start < rowEnd && e.End > rowStart;
    }
}
