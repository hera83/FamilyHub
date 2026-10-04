namespace FamilyHub.Core.Dexcom;

/// <summary>Which way the glucose is heading – Dexcom's own trend names, as the API sends them.</summary>
public enum GlucoseTrend
{
    /// <summary>No trend, or one this version does not know.</summary>
    Unknown,

    DoubleUp,
    SingleUp,
    FortyFiveUp,
    Flat,
    FortyFiveDown,
    SingleDown,
    DoubleDown,

    /// <summary>The sensor could not work out a trend (e.g. just after start).</summary>
    NotComputable,

    /// <summary>Changing faster than the sensor can measure.</summary>
    RateOutOfRange,
}

public static class GlucoseTrendExtensions
{
    /// <summary>The Danish words for the trend – for a line of text or a screen reader.</summary>
    public static string Label(this GlucoseTrend trend) => trend switch
    {
        GlucoseTrend.DoubleUp => "Stiger meget hurtigt",
        GlucoseTrend.SingleUp => "Stiger hurtigt",
        GlucoseTrend.FortyFiveUp => "Stiger",
        GlucoseTrend.Flat => "Stabil",
        GlucoseTrend.FortyFiveDown => "Falder",
        GlucoseTrend.SingleDown => "Falder hurtigt",
        GlucoseTrend.DoubleDown => "Falder meget hurtigt",
        GlucoseTrend.RateOutOfRange => "Ændrer sig for hurtigt til at måle",
        _ => "Ukendt retning",
    };

    /// <summary>The usual Dexcom arrow (↑↑ ↑ ↗ → ↘ ↓ ↓↓). Empty when there is no trend.</summary>
    public static string Arrow(this GlucoseTrend trend) => trend switch
    {
        GlucoseTrend.DoubleUp => "↑↑",
        GlucoseTrend.SingleUp => "↑",
        GlucoseTrend.FortyFiveUp => "↗",
        GlucoseTrend.Flat => "→",
        GlucoseTrend.FortyFiveDown => "↘",
        GlucoseTrend.SingleDown => "↓",
        GlucoseTrend.DoubleDown => "↓↓",
        _ => "",
    };
}

/// <summary>One sensor reading (Dexcom measures every 5 minutes).</summary>
public sealed record GlucoseReading
{
    /// <summary>When the sensor measured it, with the offset the API sent.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>mmol/L, e.g. 6.4.</summary>
    public required double MmolL { get; init; }

    public GlucoseTrend Trend { get; init; }
}

/// <summary>The readings in a period, oldest first.</summary>
public sealed record GlucoseReadings
{
    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    public required IReadOnlyList<GlucoseReading> Items { get; init; }
}

/// <summary>The API's own health check: is it up, and does it still get readings from Dexcom?</summary>
public sealed record DexcomStatus
{
    /// <summary>True when the API says "Healthy" (it answers 503 when readings have stopped coming).</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>The API's own word, e.g. "Healthy" or "Unhealthy".</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    public DateTimeOffset? LatestReadingAt { get; init; }

    public TimeSpan? LatestReadingAge { get; init; }

    /// <summary>Why it is not healthy (English, from the API). For the log and the settings page.</summary>
    public string? Reason { get; init; }
}
