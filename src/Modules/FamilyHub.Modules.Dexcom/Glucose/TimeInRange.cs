using FamilyHub.Core.Dexcom;

namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>How much of a period was below, inside and above the target range.</summary>
public sealed record TimeInRangeResult
{
    public static TimeInRangeResult Empty { get; } = new();

    /// <summary>Number of readings the result is based on (Dexcom measures every 5 minutes: 288 a day).</summary>
    public int Count { get; init; }

    /// <summary>Whole percent. The three always add up to 100 (when there are readings).</summary>
    public int BelowPercent { get; init; }

    public int InRangePercent { get; init; }

    public int AbovePercent { get; init; }

    /// <summary>Mean mmol/L. Null without readings.</summary>
    public double? Average { get; init; }

    /// <summary>The oldest reading in the period – tells whether the period is covered yet.</summary>
    public DateTimeOffset? FirstReading { get; init; }

    public bool HasData => Count > 0;
}

/// <summary>Time in range ("tid i målområdet"). Pure functions – unit-tested.</summary>
public static class TimeInRange
{
    /// <summary>
    /// Each reading counts the same (they come every 5 minutes), so the share of readings is the share of time.
    /// The limits are inclusive: exactly 3,9 and exactly 10,0 are in range.
    /// </summary>
    public static TimeInRangeResult Compute(IReadOnlyList<GlucoseReading> readings, GlucoseSettings settings)
    {
        if (readings.Count == 0)
        {
            return TimeInRangeResult.Empty;
        }

        int below = 0, above = 0;
        double sum = 0;
        var first = readings[0].Time;
        foreach (var reading in readings)
        {
            switch (GlucoseRules.Classify(reading.MmolL, settings))
            {
                case GlucoseLevel.Low:
                    below++;
                    break;
                case GlucoseLevel.High:
                    above++;
                    break;
            }

            sum += reading.MmolL;
            if (reading.Time < first)
            {
                first = reading.Time;
            }
        }

        var percents = WholePercents([below, readings.Count - below - above, above]);
        return new TimeInRangeResult
        {
            Count = readings.Count,
            BelowPercent = percents[0],
            InRangePercent = percents[1],
            AbovePercent = percents[2],
            Average = sum / readings.Count,
            FirstReading = first,
        };
    }

    /// <summary>
    /// How many days the result actually covers, when that is clearly less than asked for
    /// ("Bygger på 2 dages målinger"). Null when the period is (nearly) covered.
    /// </summary>
    public static int? CoveredDays(TimeInRangeResult result, DateTimeOffset now, int periodDays)
    {
        if (result.FirstReading is not { } first)
        {
            return null;
        }

        var covered = now - first;
        if (covered >= TimeSpan.FromDays(periodDays) - TimeSpan.FromHours(12))
        {
            return null;
        }

        return Math.Max(1, (int)Math.Ceiling(covered.TotalDays));
    }

    /// <summary>Whole percents that add up to exactly 100 (largest remainder), so the bar and the numbers agree.</summary>
    internal static int[] WholePercents(IReadOnlyList<int> counts)
    {
        var total = counts.Sum();
        if (total == 0)
        {
            return new int[counts.Count];
        }

        var exact = counts.Select(c => c * 100.0 / total).ToArray();
        var result = exact.Select(e => (int)Math.Floor(e)).ToArray();
        var missing = 100 - result.Sum();
        foreach (var index in Enumerable.Range(0, counts.Count).OrderByDescending(i => exact[i] - result[i]).ThenByDescending(i => counts[i]).Take(missing))
        {
            result[index]++;
        }

        return result;
    }
}
