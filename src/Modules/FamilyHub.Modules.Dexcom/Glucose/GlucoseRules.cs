using FamilyHub.Core.Dexcom;
using FamilyHub.Core.Time;

namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>Where a reading lies compared with the family's target range.</summary>
public enum GlucoseLevel
{
    Low,
    InRange,
    High,
}

/// <summary>The rules the page shows readings by. Pure functions – unit-tested.</summary>
public static class GlucoseRules
{
    /// <summary>
    /// A reading older than this is not shown as "now" – the number is replaced by a dash, like in the CGM apps.
    /// Dexcom measures every 5 minutes, so one missed reading is allowed for.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(8);

    public static GlucoseLevel Classify(double mmolL, GlucoseSettings settings) =>
        mmolL < (double)settings.LowMmolL ? GlucoseLevel.Low
        : mmolL > (double)settings.HighMmolL ? GlucoseLevel.High
        : GlucoseLevel.InRange;

    public static bool IsFresh(GlucoseReading reading, DateTimeOffset now) => now - reading.Time <= StaleAfter;

    /// <summary>"Lav", "I målområdet", "Høj" – always next to the colour, never instead of it.</summary>
    public static string Label(this GlucoseLevel level) => level switch
    {
        GlucoseLevel.Low => "Lav",
        GlucoseLevel.High => "Høj",
        _ => "I målområdet",
    };

    /// <summary>"8,5" – one decimal, Danish comma.</summary>
    public static string Format(double mmolL) => mmolL.ToString("0.0", DanishFormat.Culture);

    /// <inheritdoc cref="Format(double)"/>
    public static string Format(decimal mmolL) => mmolL.ToString("0.0", DanishFormat.Culture);

    /// <summary>"3,9–10,0 mmol/L".</summary>
    public static string RangeText(GlucoseSettings settings) => $"{Format(settings.LowMmolL)}–{Format(settings.HighMmolL)} mmol/L";

    /// <summary>"Lige nu", "4 min siden", "1 t 5 min siden", "over et døgn siden".</summary>
    public static string Age(DateTimeOffset time, DateTimeOffset now) =>
        now - time < TimeSpan.FromMinutes(1) ? "Lige nu" : Elapsed(time, now) + " siden";

    /// <summary>"0 min", "23 min", "1 t 5 min", "2 timer", "over et døgn".</summary>
    public static string Elapsed(DateTimeOffset time, DateTimeOffset now)
    {
        var age = now - time;
        if (age >= TimeSpan.FromDays(1))
        {
            return "over et døgn";
        }

        // Whole minutes passed (never rounded up), so "8 min" really is 8 minutes or more.
        return DanishFormat.Duration(TimeSpan.FromMinutes(Math.Max(0, Math.Floor(age.TotalMinutes))));
    }

    /// <summary>"21:45" today, "i går 21:45", or "fre 2. okt. 21:45" – in the household's time zone.</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(time, zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        var clock = DanishFormat.Time(TimeOnly.FromDateTime(local.DateTime));
        return (today.DayNumber - day.DayNumber) switch
        {
            0 => clock,
            1 => $"i går {clock}",
            _ => $"{DanishFormat.ShortDate(day)} {clock}",
        };
    }

    /// <summary>"1 t", "24 t" – the graph's period buttons.</summary>
    public static string HoursLabel(int hours) => $"{hours} t";

    /// <summary>"Sidste time", "Sidste 3 timer".</summary>
    public static string HoursTitle(int hours) => hours == 1 ? "Sidste time" : $"Sidste {hours} timer";
}
