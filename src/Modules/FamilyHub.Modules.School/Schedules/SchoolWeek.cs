using FamilyHub.Core.Household;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>
/// What one schedule shows in one week – the fixed timetable, or this week's lessons from the school's calendar.
/// With a link everything is bound to the calendar: also the rows, made from its recurring lesson times on every fetch
/// (the fixed timetable's own rows are kept untouched for when the link is removed). The table, the tabs and the home card
/// ask this, so they need not know the difference.
/// </summary>
public sealed class SchoolWeek
{
    private readonly IReadOnlyList<FeedEvent> events;

    private SchoolWeek(SchoolSchedule schedule, DateOnly monday, IReadOnlyList<FeedEvent> events, IReadOnlyList<FeedChange> changes)
    {
        Schedule = schedule;
        Monday = monday;
        IsoWeek = ScheduleRules.WeekOf(monday);
        this.events = events;
        Changes = changes;
    }

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
        if (!schedule.UsesFeed || feedEvents is null)
        {
            return new SchoolWeek(schedule, monday, [], []);
        }

        // The rows come from the whole calendar (not just this week), so they stay the same from week to week.
        // An empty calendar (e.g. the holidays) keeps the schedule's own rows.
        var effective = PeriodPlanner.FromEvents(feedEvents) is { } rows ? schedule with { Periods = rows } : schedule;

        var from = monday.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        var sunday = monday.AddDays(6);
        return new SchoolWeek(
            effective,
            monday,
            [.. feedEvents.Where(e => e.Start < to && e.End > from).OrderBy(e => e.Start)],
            [.. (changes ?? []).Where(c => c.Date >= monday && c.Date <= sunday)]);
    }

    public DateOnly DateOf(DayOfWeek day) => Monday.AddDays(((int)day + 6) % 7);

    /// <summary>What the cell shows: the subject (or the first lesson from the calendar, with its place and own time).</summary>
    public LessonView LessonAt(DayOfWeek day, SchoolPeriod period)
    {
        if (!IsLive)
        {
            return ScheduleRules.LessonAt(Schedule, day, period, IsoWeek);
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
                ? new LessonView(SubjectFor(cancelled.Before!.Title), cancelled.Before!.Location, false, null, OwnTime(cancelled.Before!, period), 0, FeedChangeKind.Cancelled)
                : LessonView.Empty;
        }

        var first = inRow[0];
        var change = Changes.FirstOrDefault(c => c.After is { } after && FeedChanges.KeyOf(after) == FeedChanges.KeyOf(first) && after.Start == first.Start)?.Kind;
        return new LessonView(SubjectFor(first.Title), first.Location, false, null, OwnTime(first, period), inRow.Count - 1, change);
    }

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
            return ScheduleRules.DaySpan(Schedule, day, IsoWeek);
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
            return ScheduleRules.Lessons(Schedule, day, IsoWeek);
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
        : ScheduleRules.LessonsPerWeek(Schedule, IsoWeek);

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
