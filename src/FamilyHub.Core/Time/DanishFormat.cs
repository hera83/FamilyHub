using System.Globalization;

namespace FamilyHub.Core.Time;

/// <summary>
/// Danish date/time wording used across the UI, so every module writes dates the same way.
/// </summary>
public static class DanishFormat
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("da-DK");

    /// <summary>ISO 8601 week number – the one Danish calendars use ("uge 39").</summary>
    public static int WeekNumber(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>"Godmorgen", "God eftermiddag" … based on the time of day.</summary>
    public static string Greeting(TimeOnly time) => time.Hour switch
    {
        >= 5 and < 10 => "Godmorgen",
        >= 10 and < 12 => "God formiddag",
        >= 12 and < 18 => "God eftermiddag",
        >= 18 and < 23 => "Godaften",
        _ => "Godnat",
    };

    /// <summary>"Fredag 25. september".</summary>
    public static string LongDate(DateOnly date) => Capitalize(date.ToString("dddd d. MMMM", Culture));

    /// <summary>"Fredag 25. september 2026".</summary>
    public static string LongDateWithYear(DateOnly date) => Capitalize(date.ToString("dddd d. MMMM yyyy", Culture));

    /// <summary>"fre. 25. sep." – for compact lists.</summary>
    public static string ShortDate(DateOnly date) => date.ToString("ddd d. MMM", Culture);

    /// <summary>"08:42".</summary>
    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"I dag", "I morgen", "I går" or the short date.</summary>
    public static string RelativeDay(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "I dag",
        1 => "I morgen",
        -1 => "I går",
        _ => Capitalize(ShortDate(date)),
    };

    /// <summary>"Man", "Tir" … – column headers in week and month views.</summary>
    public static string WeekdayShort(DayOfWeek day) =>
        Capitalize(Culture.DateTimeFormat.GetAbbreviatedDayName(day).TrimEnd('.'));

    /// <summary>"September 2026".</summary>
    public static string MonthYear(DateOnly date) => Capitalize(date.ToString("MMMM yyyy", Culture));

    /// <summary>"21.–27. september", "28. september – 4. oktober", "28. december 2026 – 3. januar 2027".</summary>
    public static string DayRange(DateOnly first, DateOnly last)
    {
        if (first.Year != last.Year)
        {
            return $"{first.ToString("d. MMMM yyyy", Culture)} – {last.ToString("d. MMMM yyyy", Culture)}";
        }

        return first.Month == last.Month
            ? $"{first.Day}.–{last.ToString("d. MMMM", Culture)}"
            : $"{first.ToString("d. MMMM", Culture)} – {last.ToString("d. MMMM", Culture)}";
    }

    /// <summary>"15:30–17:00".</summary>
    public static string TimeRange(TimeOnly start, TimeOnly end) => $"{Time(start)}–{Time(end)}";

    /// <summary>"15 min", "1 time", "1½ time", "2 timer", "2 t 15 min".</summary>
    public static string Duration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes);
        var hours = minutes / 60;
        var rest = minutes % 60;
        return (hours, rest) switch
        {
            (0, _) => $"{rest} min",
            (1, 0) => "1 time",
            (1, 30) => "1½ time",
            (_, 0) => $"{hours} timer",
            (_, 30) => $"{hours}½ time",
            _ => $"{hours} t {rest} min",
        };
    }

    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture) + text[1..];
}
