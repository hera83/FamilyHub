using FamilyHub.Core.Configuration;
using FamilyHub.Core.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Core;

public class DanishFormatTests
{
    [Theory]
    [InlineData("2026-09-25", 39)]
    [InlineData("2026-01-01", 1)]
    [InlineData("2027-01-01", 53)] // 2026 has 53 ISO weeks
    [InlineData("2027-01-04", 1)]
    public void Week_numbers_follow_iso_8601(string date, int week)
    {
        Assert.Equal(week, DanishFormat.WeekNumber(DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("04:59", "Godnat")]
    [InlineData("05:00", "Godmorgen")]
    [InlineData("09:59", "Godmorgen")]
    [InlineData("10:00", "God formiddag")]
    [InlineData("12:00", "God eftermiddag")]
    [InlineData("18:00", "Godaften")]
    [InlineData("23:00", "Godnat")]
    public void Greeting_follows_the_time_of_day(string time, string greeting)
    {
        Assert.Equal(greeting, DanishFormat.Greeting(TimeOnly.Parse(time)));
    }

    [Fact]
    public void Dates_are_written_the_danish_way()
    {
        var date = new DateOnly(2026, 9, 25);

        Assert.Equal("Fredag 25. september", DanishFormat.LongDate(date));
        Assert.Equal("Fredag 25. september 2026", DanishFormat.LongDateWithYear(date));
        Assert.Equal("08:05", DanishFormat.Time(new TimeOnly(8, 5)));
    }

    [Fact]
    public void Relative_days_use_words_near_today()
    {
        var today = new DateOnly(2026, 9, 25);

        Assert.Equal("I dag", DanishFormat.RelativeDay(today, today));
        Assert.Equal("I morgen", DanishFormat.RelativeDay(today.AddDays(1), today));
        Assert.Equal("I går", DanishFormat.RelativeDay(today.AddDays(-1), today));
        Assert.StartsWith("Man", DanishFormat.RelativeDay(new DateOnly(2026, 9, 28), today));
    }

    [Fact]
    public void Clock_uses_the_configured_time_zone_and_ticks_every_minute()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 6, 41, 30, TimeSpan.Zero));
        var options = Options.Create(new FamilyHubOptions { TimeZone = "Europe/Copenhagen" });
        using var clock = new HubClock(time, options, NullLogger<HubClock>.Instance);

        Assert.Equal(new TimeOnly(8, 41, 30), TimeOnly.FromDateTime(clock.Now.DateTime)); // CEST = UTC+2

        var ticks = new List<DateTimeOffset>();
        using (clock.SubscribeMinuteTick(ticks.Add))
        {
            time.Advance(TimeSpan.FromSeconds(29));
            Assert.Empty(ticks);

            time.Advance(TimeSpan.FromSeconds(2)); // just past the minute boundary
            Assert.Single(ticks);
            Assert.Equal((8, 42), (ticks[0].Hour, ticks[0].Minute));

            time.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(2, ticks.Count);
        }

        for (var i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(61));
        }

        Assert.Equal(2, ticks.Count); // unsubscribed
    }

    [Fact]
    public void A_failing_tick_subscriber_does_not_stop_the_clock()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 6, 0, 0, TimeSpan.Zero));
        using var clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        var calls = 0;
        using var broken = clock.SubscribeMinuteTick(_ => throw new InvalidOperationException("boom"));
        using var healthy = clock.SubscribeMinuteTick(_ => calls++);

        for (var i = 0; i < 3; i++)
        {
            time.Advance(TimeSpan.FromSeconds(61));
        }

        Assert.Equal(3, calls);
    }

    [Fact]
    public void Calendar_headings_are_written_the_danish_way()
    {
        Assert.Equal("Man", DanishFormat.WeekdayShort(DayOfWeek.Monday));
        Assert.Equal("Søn", DanishFormat.WeekdayShort(DayOfWeek.Sunday));
        Assert.Equal("September 2026", DanishFormat.MonthYear(new DateOnly(2026, 9, 26)));
        Assert.Equal("15:30–17:00", DanishFormat.TimeRange(new TimeOnly(15, 30), new TimeOnly(17, 0)));
    }

    [Theory]
    [InlineData("2026-09-21", "2026-09-27", "21.–27. september")]
    [InlineData("2026-09-28", "2026-10-04", "28. september – 4. oktober")]
    [InlineData("2026-12-28", "2027-01-03", "28. december 2026 – 3. januar 2027")]
    public void Day_ranges_only_repeat_what_changes(string first, string last, string expected)
    {
        Assert.Equal(expected, DanishFormat.DayRange(DateOnly.Parse(first), DateOnly.Parse(last)));
    }

    [Theory]
    [InlineData(15, "15 min")]
    [InlineData(60, "1 time")]
    [InlineData(90, "1½ time")]
    [InlineData(120, "2 timer")]
    [InlineData(150, "2½ time")]
    [InlineData(135, "2 t 15 min")]
    public void Durations_read_naturally(int minutes, string expected)
    {
        Assert.Equal(expected, DanishFormat.Duration(TimeSpan.FromMinutes(minutes)));
    }
}
