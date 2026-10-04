using System.Globalization;
using System.Text;
using FamilyHub.Core.Dexcom;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Dexcom.Glucose;

namespace FamilyHub.Modules.Dexcom.Components;

/// <summary>A reading's place in the graph: X and Y in percent of the plot (0,0 = top left).</summary>
internal sealed record ChartDot(double X, double Y, GlucoseLevel Level);

/// <summary>A label on an axis: <see cref="Position"/> in percent (X for times, Y from the top for values).</summary>
internal sealed record ChartTick(double Position, string Label, bool Strong = false);

internal sealed record GlucoseChartModel
{
    public required double Top { get; init; }

    public required double Bottom { get; init; }

    /// <summary>The target range's edges, in percent from the top.</summary>
    public required double HighY { get; init; }

    public required double LowY { get; init; }

    public required IReadOnlyList<ChartDot> Dots { get; init; }

    public required IReadOnlyList<ChartTick> TimeTicks { get; init; }

    public required IReadOnlyList<ChartTick> ValueTicks { get; init; }

    /// <summary>SVG path data per level – one dot per reading ("M x y h0" draws a round dot with round line caps).</summary>
    public string DotPath(GlucoseLevel level)
    {
        var path = new StringBuilder();
        foreach (var dot in Dots.Where(d => d.Level == level))
        {
            path.Append('M').Append(GlucoseChartLayout.Number(dot.X)).Append(' ').Append(GlucoseChartLayout.Number(dot.Y)).Append("h0");
        }

        return path.ToString();
    }
}

/// <summary>The glucose graph's geometry. Pure functions – unit-tested; the component only draws.</summary>
internal static class GlucoseChartLayout
{
    /// <summary>The graph always starts at 2 mmol/L (Dexcom reads down to 2,2 – "LOW").</summary>
    public const double Bottom = 2;

    /// <summary>The top grows in fixed steps, so the graph does not jump about with every reading.</summary>
    private static readonly double[] Tops = [14, 18, 22];

    public static GlucoseChartModel Build(
        IReadOnlyList<GlucoseReading> readings, DateTimeOffset from, DateTimeOffset to, GlucoseSettings settings, TimeZoneInfo zone)
    {
        var max = readings.Count == 0 ? 0 : readings.Max(r => r.MmolL);
        var top = Top(max, settings);
        var span = (to - from).TotalSeconds;

        double ToY(double mmolL) => (top - Math.Clamp(mmolL, Bottom, top)) / (top - Bottom) * 100;

        var dots = readings
            .Where(r => r.Time >= from && r.Time <= to)
            .Select(r => new ChartDot((r.Time - from).TotalSeconds / span * 100, ToY(r.MmolL), GlucoseRules.Classify(r.MmolL, settings)))
            .ToList();

        return new GlucoseChartModel
        {
            Top = top,
            Bottom = Bottom,
            HighY = ToY((double)settings.HighMmolL),
            LowY = ToY((double)settings.LowMmolL),
            Dots = dots,
            TimeTicks = TimeTicks(from, to, zone),
            ValueTicks =
            [
                new(ToY(top), Label(top)),
                new(ToY((double)settings.HighMmolL), GlucoseRules.Format(settings.HighMmolL), Strong: true),
                new(ToY((double)settings.LowMmolL), GlucoseRules.Format(settings.LowMmolL), Strong: true),
            ],
        };
    }

    /// <summary>14, 18 or 22 mmol/L – the smallest that has room for the readings and some air above the high limit.</summary>
    public static double Top(double maxReading, GlucoseSettings settings)
    {
        var needed = Math.Max(maxReading, (double)settings.HighMmolL + 2);
        foreach (var top in Tops)
        {
            if (needed <= top)
            {
                return top;
            }
        }

        return Math.Ceiling(needed);
    }

    /// <summary>
    /// Whole clock times along the bottom: every 15 min for 1 hour … every 3 hours for 24 hours.
    /// None right at the edges, where the label would be cut off.
    /// </summary>
    public static IReadOnlyList<ChartTick> TimeTicks(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var span = to - from;
        var step = span.TotalHours switch
        {
            <= 1.5 => TimeSpan.FromMinutes(15),
            <= 3.5 => TimeSpan.FromMinutes(30),
            <= 7 => TimeSpan.FromHours(1),
            <= 13 => TimeSpan.FromHours(2),
            _ => TimeSpan.FromHours(3),
        };

        // The first whole step after "from" on the household's clock (e.g. 18:30, 21:00).
        var localFrom = TimeZoneInfo.ConvertTime(from, zone);
        var midnight = new DateTimeOffset(localFrom.Date, localFrom.Offset);
        var steps = Math.Ceiling((localFrom - midnight).TotalMinutes / step.TotalMinutes);
        var tick = midnight + TimeSpan.FromMinutes(steps * step.TotalMinutes);

        var ticks = new List<ChartTick>();
        for (; tick <= to; tick += step)
        {
            var x = (tick - from).TotalSeconds / span.TotalSeconds * 100;
            if (x is >= 3 and <= 97)
            {
                var local = TimeZoneInfo.ConvertTime(tick, zone);
                ticks.Add(new ChartTick(x, DanishFormat.Time(TimeOnly.FromDateTime(local.DateTime))));
            }
        }

        return ticks;
    }

    /// <summary>Invariant, two decimals – for SVG attributes and inline percentages.</summary>
    public static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Label(double mmolL) => mmolL.ToString("0", DanishFormat.Culture);
}
