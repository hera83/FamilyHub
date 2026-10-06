using System.Globalization;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Feeds;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>
/// Editing the timetable's rows without a keyboard: times move in 5-minute steps, and the rows after a change
/// can move along, so a longer lesson or a new break never leaves gaps or overlaps behind it.
/// </summary>
public static class PeriodPlanner
{
    public const int StepMinutes = 5;
    public const int DefaultLessonMinutes = 45;
    public const int DefaultBreakMinutes = 20;
    public const int MaxMinutes = 180;

    /// <summary>Rows made from a calendar – more would not fit on the screen.</summary>
    public const int MaxFeedRows = 8;

    /// <summary>The earliest and latest start a row can get.</summary>
    public const int EarliestStart = 6 * 60;
    public const int LatestStart = 18 * 60;

    /// <summary>Rows never run past midnight, however far they are moved.</summary>
    private const int LastMinute = 23 * 60 + 55;

    /// <summary>The names offered for a break.</summary>
    public static IReadOnlyList<string> BreakLabels { get; } = ["Frikvarter", "Spisepause", "Formiddagsmad", "Frokost", "Pause"];

    /// <summary>A common day for the kind of school – the family adjusts it to their own.</summary>
    public static IReadOnlyList<SchoolPeriod> StandardDay(SchoolLevel level = SchoolLevel.PrimarySchool) => level switch
    {
        SchoolLevel.Gymnasium => GymnasiumDay(),
        SchoolLevel.University => UniversityDay(),
        _ => PrimarySchoolDay(),
    };

    /// <summary>Folkeskole: seven lessons of 45 minutes with three breaks.</summary>
    private static IReadOnlyList<SchoolPeriod> PrimarySchoolDay() =>
    [
        Lesson("08:00", "08:45"),
        Lesson("08:45", "09:30"),
        Break("09:30", "09:50", "Frikvarter"),
        Lesson("09:50", "10:35"),
        Lesson("10:35", "11:20"),
        Break("11:20", "11:50", "Spisepause"),
        Lesson("11:50", "12:35"),
        Lesson("12:35", "13:20"),
        Break("13:20", "13:30", "Frikvarter"),
        Lesson("13:30", "14:15"),
    ];

    /// <summary>Gymnasium: four modules of 90 minutes.</summary>
    private static IReadOnlyList<SchoolPeriod> GymnasiumDay() =>
    [
        Lesson("08:15", "09:45"),
        Break("09:45", "10:00", "Pause"),
        Lesson("10:00", "11:30"),
        Break("11:30", "12:00", "Frokost"),
        Lesson("12:00", "13:30"),
        Break("13:30", "13:45", "Pause"),
        Lesson("13:45", "15:15"),
    ];

    /// <summary>Universitet: four blocks of 1¾ hours (lectures and classes).</summary>
    private static IReadOnlyList<SchoolPeriod> UniversityDay() =>
    [
        Lesson("08:15", "10:00"),
        Break("10:00", "10:15", "Pause"),
        Lesson("10:15", "12:00"),
        Break("12:00", "12:45", "Frokost"),
        Lesson("12:45", "14:30"),
        Break("14:30", "14:45", "Pause"),
        Lesson("14:45", "16:30"),
    ];

    /// <summary>
    /// The rows of a schedule made from a calendar's lessons: the most common time slots that don't overlap, at most
    /// eight, with a »Pause« in every gap – the calendar doesn't say what a gap is, so it is never guessed. A slot must recur (a quarter as often as the busiest one),
    /// so a single meeting doesn't become a row. An odd lesson still shows in the row it overlaps; one that overlaps none
    /// is listed under the table. Returns null when the calendar has no lessons on school days.
    /// </summary>
    public static IReadOnlyList<SchoolPeriod>? FromEvents(IEnumerable<FeedEvent> events)
    {
        var slots = events
            .Where(e => ScheduleRules.IsSchoolDay(e.Start.DayOfWeek) && DateOnly.FromDateTime(e.Start) == DateOnly.FromDateTime(e.End))
            .GroupBy(e => (Start: ToMinutes(TimeOnly.FromDateTime(e.Start)), End: ToMinutes(TimeOnly.FromDateTime(e.End))))
            .Select(g => (g.Key.Start, g.Key.End, Count: g.Count()))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Start)
            .ThenByDescending(s => s.End - s.Start)
            .ToList();

        var minCount = slots.Count == 0 ? 1 : Math.Max(1, slots[0].Count / 4);
        var chosen = new List<(int Start, int End)>();
        foreach (var slot in slots.Where(s => s.Count >= minCount))
        {
            if (chosen.Count < MaxFeedRows && chosen.All(c => slot.End <= c.Start || slot.Start >= c.End))
            {
                chosen.Add((slot.Start, slot.End));
            }
        }

        if (chosen.Count == 0)
        {
            return null;
        }

        var rows = new List<SchoolPeriod>();
        foreach (var (start, end) in chosen.OrderBy(c => c.Start))
        {
            if (rows.Count > 0 && ToMinutes(rows[^1].End) is var gapStart && start - gapStart >= StepMinutes)
            {
                rows.Add(new SchoolPeriod { Kind = PeriodKind.Break, Start = FromMinutes(gapStart), End = FromMinutes(start), Label = "Pause" });
            }

            rows.Add(new SchoolPeriod { Kind = PeriodKind.Lesson, Start = FromMinutes(start), End = FromMinutes(end) });
        }

        return rows;
    }

    /// <summary>The same times with new ids – for "Brug tiderne fra Emmas skema".</summary>
    public static IReadOnlyList<SchoolPeriod> CopyOf(IEnumerable<SchoolPeriod> periods) =>
        [.. periods.Select(p => p with { Id = Guid.NewGuid() })];

    public static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    public static TimeOnly FromMinutes(int minutes) => new TimeOnly(0, 0).AddMinutes(Math.Clamp(minutes, 0, LastMinute));

    /// <summary>"08:45" for the steppers.</summary>
    public static string FormatMinutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    /// <summary>"45 min", "1 time", "1½ time".</summary>
    public static string FormatDuration(int minutes) => DanishFormat.Duration(TimeSpan.FromMinutes(minutes));

    /// <summary>
    /// Gives a row a new start, length (and label for a break). With <paramref name="moveFollowing"/> every row after it
    /// moves as much as its end moved.
    /// </summary>
    public static IReadOnlyList<SchoolPeriod> Change(IReadOnlyList<SchoolPeriod> periods, Guid id, int startMinutes, int minutes, string? label, bool moveFollowing)
    {
        var ordered = Ordered(periods);
        var index = ordered.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return ordered;
        }

        var old = ordered[index];
        var start = Math.Clamp(startMinutes, EarliestStart, LatestStart);
        var end = Math.Min(start + Math.Clamp(minutes, StepMinutes, MaxMinutes), LastMinute);
        ordered[index] = old with
        {
            Start = FromMinutes(start),
            End = FromMinutes(end),
            Label = old.IsBreak ? label ?? old.Label : null,
        };

        if (moveFollowing)
        {
            Move(ordered, index + 1, end - ToMinutes(old.End));
        }

        return Ordered(ordered);
    }

    /// <summary>A new lesson right after the last row, as long as the last lesson (45 minutes when there is none).</summary>
    public static IReadOnlyList<SchoolPeriod> AddLesson(IReadOnlyList<SchoolPeriod> periods)
    {
        var ordered = Ordered(periods);
        var minutes = ordered.LastOrDefault(p => !p.IsBreak)?.Minutes ?? DefaultLessonMinutes;
        var start = ordered.Count == 0 ? 8 * 60 : ToMinutes(ordered[^1].End);
        ordered.Add(new SchoolPeriod
        {
            Kind = PeriodKind.Lesson,
            Start = FromMinutes(start),
            End = FromMinutes(start + minutes),
        });
        return ordered;
    }

    /// <summary>
    /// A break right after the row <paramref name="afterId"/> (or first, when null). With <paramref name="moveFollowing"/>
    /// the rows after it move later by the length of the break.
    /// </summary>
    public static IReadOnlyList<SchoolPeriod> InsertBreak(IReadOnlyList<SchoolPeriod> periods, Guid? afterId, string label, int minutes, bool moveFollowing)
    {
        var ordered = Ordered(periods);
        var index = afterId is { } id ? ordered.FindIndex(p => p.Id == id) : -1;
        var start = index >= 0 ? ToMinutes(ordered[index].End) : ordered.Count > 0 ? ToMinutes(ordered[0].Start) : 8 * 60;
        var length = Math.Clamp(minutes, StepMinutes, MaxMinutes);

        if (moveFollowing)
        {
            Move(ordered, index + 1, length);
        }

        ordered.Insert(index + 1, new SchoolPeriod
        {
            Kind = PeriodKind.Break,
            Start = FromMinutes(start),
            End = FromMinutes(start + length),
            Label = label,
        });
        return Ordered(ordered);
    }

    public static IReadOnlyList<SchoolPeriod> Remove(IReadOnlyList<SchoolPeriod> periods, Guid id) => [.. Ordered(periods).Where(p => p.Id != id)];

    private static List<SchoolPeriod> Ordered(IEnumerable<SchoolPeriod> periods) => [.. periods.OrderBy(p => p.Start).ThenBy(p => p.End)];

    private static void Move(List<SchoolPeriod> ordered, int fromIndex, int deltaMinutes)
    {
        if (deltaMinutes == 0)
        {
            return;
        }

        for (var i = fromIndex; i < ordered.Count; i++)
        {
            var p = ordered[i];
            ordered[i] = p with
            {
                Start = FromMinutes(ToMinutes(p.Start) + deltaMinutes),
                End = FromMinutes(ToMinutes(p.End) + deltaMinutes),
            };
        }
    }

    private static SchoolPeriod Lesson(string start, string end) =>
        new() { Kind = PeriodKind.Lesson, Start = TimeOnly.Parse(start, CultureInfo.InvariantCulture), End = TimeOnly.Parse(end, CultureInfo.InvariantCulture) };

    private static SchoolPeriod Break(string start, string end, string label) =>
        new() { Kind = PeriodKind.Break, Start = TimeOnly.Parse(start, CultureInfo.InvariantCulture), End = TimeOnly.Parse(end, CultureInfo.InvariantCulture), Label = label };
}
