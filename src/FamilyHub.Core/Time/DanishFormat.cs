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

    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture) + text[1..];
}
