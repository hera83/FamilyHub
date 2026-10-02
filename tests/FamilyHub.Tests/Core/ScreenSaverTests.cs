using FamilyHub.Core.Devices;

namespace FamilyHub.Tests.Core;

public class ScreenSaverTests
{
    private static readonly DateTimeOffset Evening = new(2026, 10, 2, 21, 0, 0, TimeSpan.FromHours(2));

    private static IEnumerable<ScreenSaverPosition> OneNight() =>
        Enumerable.Range(0, 12 * 60).Select(minute => ScreenSaverLayout.ClockPosition(Evening.AddMinutes(minute)));

    [Fact]
    public void Clock_never_comes_close_to_an_edge()
    {
        foreach (var position in OneNight())
        {
            Assert.InRange(position.LeftPercent, ScreenSaverLayout.Margin, 100 - ScreenSaverLayout.Margin);
            Assert.InRange(position.TopPercent, ScreenSaverLayout.Margin, 100 - ScreenSaverLayout.Margin);
        }
    }

    [Fact]
    public void Clock_moves_only_a_little_from_one_minute_to_the_next()
    {
        var positions = OneNight().ToList();

        foreach (var (before, after) in positions.Zip(positions.Skip(1)))
        {
            Assert.True(Math.Abs(after.LeftPercent - before.LeftPercent) <= 3, $"{before} -> {after}");
            Assert.True(Math.Abs(after.TopPercent - before.TopPercent) <= 3, $"{before} -> {after}");
        }
    }

    [Fact]
    public void Clock_visits_the_whole_screen_during_a_night()
    {
        var positions = OneNight().ToList();

        Assert.True(positions.Min(p => p.LeftPercent) < 20 && positions.Max(p => p.LeftPercent) > 80);
        Assert.True(positions.Min(p => p.TopPercent) < 20 && positions.Max(p => p.TopPercent) > 80);
    }

    [Fact]
    public void Same_minute_gives_the_same_place_so_redraws_do_not_jump()
    {
        Assert.Equal(
            ScreenSaverLayout.ClockPosition(Evening.AddSeconds(5)),
            ScreenSaverLayout.ClockPosition(Evening.AddSeconds(55)));
    }
}
