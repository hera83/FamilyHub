using FamilyHub.Core.Dexcom;
using FamilyHub.Modules.Dexcom.Components;
using FamilyHub.Modules.Dexcom.Glucose;

namespace FamilyHub.Tests.Dexcom;

public sealed class GlucoseLogicTests
{
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 21, 30, 0, Cest);
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
    private static readonly GlucoseSettings Standard = new();

    private static GlucoseReading Reading(DateTimeOffset time, double mmolL, GlucoseTrend trend = GlucoseTrend.Flat) =>
        new() { Time = time, MmolL = mmolL, Trend = trend };

    /// <summary>One reading every 5 minutes back from <see cref="Now"/>, oldest first.</summary>
    private static GlucoseReading[] Every5Minutes(params double[] values) =>
        [.. values.Select((v, i) => Reading(Now.AddMinutes(-5 * (values.Length - 1 - i)), v))];

    // ------------------------------------------------------------------ rules

    [Theory]
    [InlineData(3.8, GlucoseLevel.Low)]
    [InlineData(3.9, GlucoseLevel.InRange)]
    [InlineData(10.0, GlucoseLevel.InRange)]
    [InlineData(10.1, GlucoseLevel.High)]
    public void The_limits_themselves_are_in_range(double mmolL, GlucoseLevel level) =>
        Assert.Equal(level, GlucoseRules.Classify(mmolL, Standard));

    [Fact]
    public void The_familys_own_limits_are_used()
    {
        var tight = new GlucoseSettings { LowMmolL = 4.5m, HighMmolL = 8.0m };

        Assert.Equal(GlucoseLevel.Low, GlucoseRules.Classify(4.4, tight));
        Assert.Equal(GlucoseLevel.High, GlucoseRules.Classify(8.1, tight));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(8, true)]
    [InlineData(8.1, false)]
    [InlineData(25, false)]
    public void A_reading_counts_as_now_for_8_minutes(double minutesOld, bool fresh) =>
        Assert.Equal(fresh, GlucoseRules.IsFresh(Reading(Now.AddMinutes(-minutesOld), 6), Now));

    [Theory]
    [InlineData(0.5, "Lige nu")]
    [InlineData(1, "1 min siden")]
    [InlineData(7.9, "7 min siden")]
    [InlineData(65, "1 t 5 min siden")]
    [InlineData(120, "2 timer siden")]
    [InlineData(60 * 25, "over et døgn siden")]
    public void Age_is_whole_minutes_never_rounded_up(double minutesOld, string text) =>
        Assert.Equal(text, GlucoseRules.Age(Now.AddMinutes(-minutesOld), Now));

    [Fact]
    public void A_reading_stamped_ahead_of_our_clock_is_just_now()
    {
        Assert.Equal("Lige nu", GlucoseRules.Age(Now.AddMinutes(2), Now));
        Assert.Equal("0 min", GlucoseRules.Elapsed(Now.AddMinutes(2), Now));
    }

    [Fact]
    public void When_is_the_clock_today_and_says_the_day_otherwise()
    {
        Assert.Equal("21:20", GlucoseRules.When(Now.AddMinutes(-10), Now, Copenhagen));
        Assert.Equal("21:20", GlucoseRules.When(Now.AddMinutes(-10).ToUniversalTime(), Now, Copenhagen));
        Assert.Equal("i går 23:00", GlucoseRules.When(new DateTimeOffset(2026, 10, 2, 23, 0, 0, Cest), Now, Copenhagen));
        var older = GlucoseRules.When(new DateTimeOffset(2026, 10, 1, 9, 0, 0, Cest), Now, Copenhagen);
        Assert.Contains("1. okt", older, StringComparison.Ordinal);
        Assert.EndsWith("09:00", older, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_are_danish_with_one_decimal()
    {
        Assert.Equal("8,0", GlucoseRules.Format(8.0));
        Assert.Equal("3,9–10,0 mmol/L", GlucoseRules.RangeText(Standard));
    }

    [Fact]
    public void Settings_from_a_hand_edited_file_are_kept_within_limits()
    {
        var odd = new GlucoseSettings { LowMmolL = 1.23m, HighMmolL = 30m, ChartHours = 5 }.Normalized();

        Assert.Equal(GlucoseSettings.MinLow, odd.LowMmolL);
        Assert.Equal(GlucoseSettings.MaxHigh, odd.HighMmolL);
        Assert.Equal(24, odd.ChartHours);
        Assert.Equal(4.3m, new GlucoseSettings { LowMmolL = 4.26m }.Normalized().LowMmolL);
    }

    // ------------------------------------------------------------------ time in range

    [Fact]
    public void Time_in_range_counts_below_inside_and_above()
    {
        var readings = Every5Minutes(3.5, 4.0, 6.0, 9.9, 10.0, 10.5, 12.0, 7.0, 3.9, 5.0);

        var result = TimeInRange.Compute(readings, Standard);

        Assert.Equal(10, result.Count);
        Assert.Equal(10, result.BelowPercent);
        Assert.Equal(70, result.InRangePercent);
        Assert.Equal(20, result.AbovePercent);
        Assert.Equal(7.18, result.Average!.Value, precision: 2);
        Assert.Equal(readings[0].Time, result.FirstReading);
    }

    [Fact]
    public void The_three_percents_always_add_up_to_100()
    {
        var result = TimeInRange.Compute(Every5Minutes(3, 6, 12), Standard);

        Assert.Equal(100, result.BelowPercent + result.InRangePercent + result.AbovePercent);
        Assert.Equal(100, TimeInRange.WholePercents([1, 1, 1]).Sum());
        Assert.Equal([0, 100, 0], TimeInRange.WholePercents([0, 7, 0]));
        Assert.Equal([1, 98, 1], TimeInRange.WholePercents([1, 98, 1]));
    }

    [Fact]
    public void No_readings_is_no_result()
    {
        var result = TimeInRange.Compute([], Standard);

        Assert.False(result.HasData);
        Assert.Null(TimeInRange.CoveredDays(result, Now, 90));
    }

    [Theory]
    [InlineData(2.0, 90, 2)]
    [InlineData(0.2, 7, 1)]
    [InlineData(6.6, 7, null)]
    [InlineData(89.9, 90, null)]
    public void It_says_when_the_period_is_not_covered_yet(double daysOfData, int periodDays, int? covered)
    {
        var result = TimeInRange.Compute([Reading(Now.AddDays(-daysOfData), 6), Reading(Now, 6)], Standard);

        Assert.Equal(covered, TimeInRange.CoveredDays(result, Now, periodDays));
    }

    // ------------------------------------------------------------------ history

    [Fact]
    public void A_late_reading_lands_in_its_place_and_the_window_is_replaced()
    {
        var existing = Every5Minutes(5, 6, 7, 8);
        var late = Reading(Now.AddMinutes(-7), 6.5);
        var fetched = new[] { existing[1], late, existing[2] with { MmolL = 7.2 }, existing[3] };

        var merged = GlucoseHistory.Merge(existing, Now.AddMinutes(-14), Now.AddMinutes(1), fetched, Now.AddDays(-90));

        Assert.Equal([5, 6, 6.5, 7.2, 8], merged.Select(r => r.MmolL));
        Assert.Equal(merged.OrderBy(r => r.Time), merged);
    }

    [Fact]
    public void A_reading_inside_the_window_the_api_no_longer_has_is_dropped_but_not_one_on_the_edge()
    {
        var existing = Every5Minutes(5, 6, 7, 8);
        var from = existing[0].Time;

        var merged = GlucoseHistory.Merge(existing, from, Now, [existing[3]], Now.AddDays(-90));

        Assert.Equal([5, 8], merged.Select(r => r.MmolL));
    }

    [Fact]
    public void The_same_instant_with_another_offset_is_the_same_reading()
    {
        var existing = Every5Minutes(5);
        var again = existing[0] with { Time = existing[0].Time.ToUniversalTime(), MmolL = 5.1 };

        var merged = GlucoseHistory.Merge(existing, Now.AddHours(-3), Now.AddHours(-2), [again], Now.AddDays(-90));

        Assert.Equal(5.1, Assert.Single(merged).MmolL);
    }

    [Fact]
    public void Readings_older_than_the_history_are_dropped()
    {
        var old = Reading(Now.AddDays(-91), 6);

        var merged = GlucoseHistory.Merge([old], Now.AddHours(-1), Now, [Reading(Now.AddDays(-95), 6), Reading(Now, 7)], Now.AddDays(-90));

        Assert.Equal(7, Assert.Single(merged).MmolL);
    }

    [Fact]
    public void Slice_takes_the_period_including_both_ends()
    {
        var readings = Every5Minutes(1, 2, 3, 4, 5);

        Assert.Equal([2, 3, 4], GlucoseHistory.Slice(readings, readings[1].Time, readings[3].Time).Select(r => r.MmolL));
        Assert.Empty(GlucoseHistory.Slice(readings, Now.AddHours(1), Now.AddHours(2)));
        Assert.Empty(GlucoseHistory.Slice([], Now.AddHours(-1), Now));
    }

    // ------------------------------------------------------------------ graph

    [Theory]
    [InlineData(9.0, 14)]
    [InlineData(13.9, 14)]
    [InlineData(14.5, 18)]
    [InlineData(21.0, 22)]
    [InlineData(22.2, 23)]
    public void The_top_of_the_graph_grows_in_fixed_steps(double max, double top) =>
        Assert.Equal(top, GlucoseChartLayout.Top(max, Standard));

    [Fact]
    public void A_high_limit_gets_air_above_it()
    {
        Assert.Equal(18, GlucoseChartLayout.Top(5, new GlucoseSettings { HighMmolL = 13m }));
    }

    [Fact]
    public void Dots_are_placed_by_time_and_value_and_coloured_by_range()
    {
        var from = Now.AddHours(-1);
        var readings = new[] { Reading(from, 2), Reading(Now.AddMinutes(-30), 8), Reading(Now, 14), Reading(Now.AddHours(-2), 5) };

        var model = GlucoseChartLayout.Build(readings, from, Now, Standard, Copenhagen);

        Assert.Equal(14, model.Top);
        Assert.Equal(3, model.Dots.Count);
        Assert.Equal(new ChartDot(0, 100, GlucoseLevel.Low), model.Dots[0]);
        Assert.Equal(new ChartDot(50, 50, GlucoseLevel.InRange), model.Dots[1]);
        Assert.Equal(new ChartDot(100, 0, GlucoseLevel.High), model.Dots[2]);
        Assert.Equal("M50 50h0", model.DotPath(GlucoseLevel.InRange));
        Assert.Equal((14 - 10) / 12.0 * 100, model.HighY, precision: 6);
        Assert.Equal((14 - 3.9) / 12.0 * 100, model.LowY, precision: 6);
    }

    [Fact]
    public void Values_outside_the_graph_sit_on_its_edge()
    {
        var model = GlucoseChartLayout.Build([Reading(Now, 1.5), Reading(Now.AddMinutes(-5), 30)], Now.AddHours(-1), Now, Standard, Copenhagen);

        Assert.Equal(100, model.Dots[0].Y);
        Assert.Equal(0, model.Dots[1].Y);
    }

    [Fact]
    public void Three_hours_get_a_label_every_half_hour_on_the_households_clock()
    {
        var ticks = GlucoseChartLayout.TimeTicks(Now.AddHours(-3).AddMinutes(-7), Now.AddMinutes(-7), Copenhagen);

        Assert.Equal(["18:30", "19:00", "19:30", "20:00", "20:30", "21:00"], ticks.Select(t => t.Label));
        Assert.All(ticks, t => Assert.InRange(t.Position, 3, 97));
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(6, 60)]
    [InlineData(12, 120)]
    [InlineData(24, 180)]
    public void Longer_periods_get_fewer_labels(int hours, int minutesBetween)
    {
        var ticks = GlucoseChartLayout.TimeTicks(Now.AddHours(-hours), Now, Copenhagen);

        var first = TimeOnly.Parse(ticks[0].Label, System.Globalization.CultureInfo.InvariantCulture);
        var second = TimeOnly.Parse(ticks[1].Label, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(minutesBetween, (int)(second - first).TotalMinutes);
        Assert.Equal(0, first.Minute % Math.Min(minutesBetween, 60));
    }

    [Fact]
    public void Svg_numbers_are_invariant_and_short() => Assert.Equal("33.33", GlucoseChartLayout.Number(100 / 3.0));
}
