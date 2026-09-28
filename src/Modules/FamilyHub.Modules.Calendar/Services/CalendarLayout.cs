using FamilyHub.Core.Time;
using FamilyHub.Modules.Calendar.Model;

namespace FamilyHub.Modules.Calendar.Services;

/// <summary>A timed event placed in the day view's time grid.</summary>
/// <param name="StartMinute">Minutes after midnight where the block starts on this day.</param>
/// <param name="EndMinute">Minutes after midnight where it ends on this day (1440 = midnight).</param>
/// <param name="Lane">Column within a group of overlapping events (0-based).</param>
/// <param name="Lanes">Number of columns the group needs.</param>
public sealed record PositionedEvent(CalendarEvent Event, int StartMinute, int EndMinute, int Lane, int Lanes);

/// <summary>Date maths and layout for the day, week and month views. Pure functions – unit-tested.</summary>
public static class CalendarLayout
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>Short events still get a block tall enough to read and tap (40 min × 4.5rem/hour = the 48px touch minimum).</summary>
    public const int MinimumBlockMinutes = 40;

    /// <summary>Monday of the (Danish, ISO) week containing <paramref name="date"/>.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static IReadOnlyList<DateOnly> WeekDays(DateOnly date)
    {
        var monday = WeekStart(date);
        return [.. Enumerable.Range(0, 7).Select(monday.AddDays)];
    }

    /// <summary>The weeks (Monday–Sunday) that cover the month of <paramref name="date"/> – 4 to 6 rows.</summary>
    public static IReadOnlyList<IReadOnlyList<DateOnly>> MonthWeeks(DateOnly date)
    {
        var first = new DateOnly(date.Year, date.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var weeks = new List<IReadOnlyList<DateOnly>>();
        for (var monday = WeekStart(first); monday <= last; monday = monday.AddDays(7))
        {
            weeks.Add(WeekDays(monday));
        }

        return weeks;
    }

    /// <summary>The days a view shows: [From, ToExclusive).</summary>
    public static (DateOnly From, DateOnly ToExclusive) Range(CalendarView view, DateOnly date) => view switch
    {
        CalendarView.Day => (date, date.AddDays(1)),
        CalendarView.Week => (WeekStart(date), WeekStart(date).AddDays(7)),
        _ => MonthRange(date),
    };

    /// <summary>The previous/next day, week or month.</summary>
    public static DateOnly Shift(CalendarView view, DateOnly date, int direction) => view switch
    {
        CalendarView.Day => date.AddDays(direction),
        CalendarView.Week => date.AddDays(7 * direction),
        _ => new DateOnly(date.Year, date.Month, 1).AddMonths(direction),
    };

    /// <summary>The view already shows <paramref name="today"/>.</summary>
    public static bool Contains(CalendarView view, DateOnly date, DateOnly today) => view switch
    {
        CalendarView.Day => date == today,
        CalendarView.Week => WeekStart(date) == WeekStart(today),
        _ => date.Year == today.Year && date.Month == today.Month,
    };

    /// <summary>Heading for the shown period: "Lørdag 26. september", "Uge 39", "September 2026".</summary>
    public static string PeriodTitle(CalendarView view, DateOnly date, DateOnly today) => view switch
    {
        CalendarView.Day => date.Year == today.Year ? DanishFormat.LongDate(date) : DanishFormat.LongDateWithYear(date),
        CalendarView.Week => $"Uge {DanishFormat.WeekNumber(date)}",
        _ => DanishFormat.MonthYear(date),
    };

    /// <summary>Second line under the heading: "I dag", "21.–27. september" …</summary>
    public static string? PeriodDetail(CalendarView view, DateOnly date, DateOnly today)
    {
        switch (view)
        {
            case CalendarView.Day:
                var relative = DanishFormat.RelativeDay(date, today);
                return Math.Abs(date.DayNumber - today.DayNumber) <= 1 ? relative : $"Uge {DanishFormat.WeekNumber(date)}";
            case CalendarView.Week:
                var days = WeekDays(date);
                var range = DanishFormat.DayRange(days[0], days[6]);
                return days[0].Year == today.Year && days[6].Year == today.Year ? range : $"{range} {days[6].Year}";
            default:
                return null;
        }
    }

    /// <summary>
    /// Shown in the all-day row on <paramref name="day"/>: all-day events, and timed events that cover the
    /// whole day (the middle of a trip).
    /// </summary>
    public static bool IsAllDayOn(CalendarEvent e, DateOnly day)
    {
        if (!e.OccursOn(day))
        {
            return false;
        }

        if (e.IsAllDay)
        {
            return true;
        }

        var (start, end) = MinutesOn(e, day);
        return start == 0 && end == MinutesPerDay;
    }

    /// <summary>The part of an event that falls on <paramref name="day"/>, in minutes after midnight.</summary>
    public static (int Start, int End) MinutesOn(CalendarEvent e, DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var start = e.Start.DateTime <= dayStart ? 0 : (int)(e.Start.DateTime - dayStart).TotalMinutes;
        var end = e.End.DateTime >= dayStart.AddDays(1) ? MinutesPerDay : (int)(e.End.DateTime - dayStart).TotalMinutes;
        return (Math.Clamp(start, 0, MinutesPerDay), Math.Clamp(end, 0, MinutesPerDay));
    }

    /// <summary>
    /// Places the timed events of one day side by side where they overlap – like a paper calendar:
    /// each group of overlapping events shares the width equally.
    /// </summary>
    public static IReadOnlyList<PositionedEvent> LayoutDay(IEnumerable<CalendarEvent> events, DateOnly day)
    {
        var items = events
            .Where(e => e.OccursOn(day) && !IsAllDayOn(e, day))
            .Select(e => (Event: e, Minutes: MinutesOn(e, day)))
            .OrderBy(x => x.Minutes.Start)
            .ThenByDescending(x => x.Minutes.End - x.Minutes.Start)
            .ToList();

        var result = new List<PositionedEvent>();
        var group = new List<(CalendarEvent Event, int Start, int End, int Lane)>();
        var laneEnds = new List<int>();
        var groupEnd = -1;

        void Flush()
        {
            result.AddRange(group.Select(g => new PositionedEvent(g.Event, g.Start, g.End, g.Lane, laneEnds.Count)));
            group.Clear();
            laneEnds.Clear();
        }

        foreach (var (e, (start, end)) in items)
        {
            // Overlap is judged on the drawn height, so short events never cover each other.
            var visualEnd = Math.Max(end, Math.Min(start + MinimumBlockMinutes, MinutesPerDay));
            if (start >= groupEnd)
            {
                Flush();
            }

            var lane = laneEnds.FindIndex(laneEnd => laneEnd <= start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(visualEnd);
            }
            else
            {
                laneEnds[lane] = visualEnd;
            }

            group.Add((e, start, end, lane));
            groupEnd = Math.Max(groupEnd, visualEnd);
        }

        Flush();
        return result;
    }

    /// <summary>Hours shown in the time grid: 7–21 by default, widened to fit earlier or later events.</summary>
    public static (int StartHour, int EndHour) HourRange(IEnumerable<PositionedEvent> events, int defaultStart = 7, int defaultEnd = 21)
    {
        var start = defaultStart;
        var end = defaultEnd;
        foreach (var e in events)
        {
            start = Math.Min(start, e.StartMinute / 60);
            var visualEnd = Math.Max(e.EndMinute, Math.Min(e.StartMinute + MinimumBlockMinutes, MinutesPerDay));
            end = Math.Max(end, (int)Math.Ceiling(visualEnd / 60.0));
        }

        return (Math.Clamp(start, 0, 23), Math.Clamp(end, start + 1, 24));
    }

    /// <summary>"15:30–17:00", "Fra 20:00", "Til 10:00" or "Hele dagen" – how the event looks on that day.</summary>
    public static string TimeLabel(CalendarEvent e, DateOnly day)
    {
        if (IsAllDayOn(e, day))
        {
            return "Hele dagen";
        }

        var (start, end) = MinutesOn(e, day);
        var startsHere = e.StartDate == day;
        var endsHere = e.End.DateTime <= day.ToDateTime(TimeOnly.MinValue).AddDays(1);
        return (startsHere, endsHere) switch
        {
            (true, true) when start == end => Clock(start),
            (true, true) => DanishFormat.TimeRange(ToTime(start), ToTime(end)),
            (true, false) => $"Fra {Clock(start)}",
            _ => $"Til {Clock(end)}",
        };
    }

    /// <summary>The full "when" for the details dialog: "Lørdag 26. september · 10:00–11:30".</summary>
    public static string WhenLabel(CalendarEvent e)
    {
        var lastDay = e.EndDate.AddDays(-1);
        if (e.IsAllDay)
        {
            return lastDay <= e.StartDate
                ? $"{DanishFormat.LongDate(e.StartDate)} · Hele dagen"
                : $"{DanishFormat.LongDate(e.StartDate)} – {Lower(DanishFormat.LongDate(lastDay))}";
        }

        var startTime = TimeOnly.FromDateTime(e.Start.DateTime);
        var endTime = TimeOnly.FromDateTime(e.End.DateTime);
        return e.End.DateTime <= e.StartDate.ToDateTime(TimeOnly.MinValue).AddDays(1)
            ? $"{DanishFormat.LongDate(e.StartDate)} · {(startTime == endTime ? DanishFormat.Time(startTime) : DanishFormat.TimeRange(startTime, endTime))}"
            : $"{DanishFormat.LongDate(e.StartDate)} {DanishFormat.Time(startTime)} – {Lower(DanishFormat.LongDate(DateOnly.FromDateTime(e.End.DateTime)))} {DanishFormat.Time(endTime)}";
    }

    private static (DateOnly, DateOnly) MonthRange(DateOnly date)
    {
        var weeks = MonthWeeks(date);
        return (weeks[0][0], weeks[^1][6].AddDays(1));
    }

    private static TimeOnly ToTime(int minutes) => minutes >= MinutesPerDay ? TimeOnly.MinValue : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));

    private static string Clock(int minutes) => DanishFormat.Time(ToTime(minutes));

    private static string Lower(string text) => string.IsNullOrEmpty(text) ? text : char.ToLower(text[0], DanishFormat.Culture) + text[1..];
}
