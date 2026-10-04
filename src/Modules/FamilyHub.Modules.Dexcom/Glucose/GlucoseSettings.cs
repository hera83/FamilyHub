namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>
/// The family's target range and how the page starts – shared by every screen (dexcom/indstillinger.json).
/// Changed under Dexcom → the gear.
/// </summary>
public sealed record GlucoseSettings
{
    /// <summary>The usual target range for type 1 diabetes: 3,9–10,0 mmol/L.</summary>
    public const decimal DefaultLow = 3.9m;
    public const decimal DefaultHigh = 10.0m;

    /// <summary>The steppers' limits. Low and high cannot meet, so the range is always valid.</summary>
    public const decimal MinLow = 3.0m;
    public const decimal MaxLow = 6.0m;
    public const decimal MinHigh = 7.0m;
    public const decimal MaxHigh = 20.0m;

    /// <summary>The graph's periods (hours).</summary>
    public static IReadOnlyList<int> ChartHourOptions { get; } = [1, 3, 6, 12, 24];

    /// <summary>Below this is low (red). Inclusive: exactly 3,9 is in range.</summary>
    public decimal LowMmolL { get; init; } = DefaultLow;

    /// <summary>Above this is high (yellow). Inclusive: exactly 10,0 is in range.</summary>
    public decimal HighMmolL { get; init; } = DefaultHigh;

    /// <summary>The graph's period when the page opens.</summary>
    public int ChartHours { get; init; } = 24;

    /// <summary>Within the limits, one decimal, and a known period – also for a file edited by hand.</summary>
    public GlucoseSettings Normalized() => this with
    {
        LowMmolL = Math.Round(Math.Clamp(LowMmolL, MinLow, MaxLow), 1),
        HighMmolL = Math.Round(Math.Clamp(HighMmolL, MinHigh, MaxHigh), 1),
        ChartHours = ChartHourOptions.Contains(ChartHours) ? ChartHours : 24,
    };
}
