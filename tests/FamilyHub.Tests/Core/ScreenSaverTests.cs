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

    // ------------------------------------------------------------------ the smaller bubble (e.g. the glucose)

    private static IEnumerable<(ScreenSaverPosition Clock, ScreenSaverBubblePosition Bubble)> ThreeNights() =>
        Enumerable.Range(0, 3 * 24 * 60).Select(minute => Evening.AddMinutes(minute))
            .Select(time => (ScreenSaverLayout.ClockPosition(time), ScreenSaverLayout.BubblePosition(time)));

    [Fact]
    public void The_bubble_never_overlaps_the_clock()
    {
        foreach (var (clock, bubble) in ThreeNights())
        {
            var (clockLeft, clockRight) = ScreenSaverLayout.ClockSpan(clock);
            var bubbleLeft = bubble.LeftPercent / 100;
            var bubbleRight = bubbleLeft + ScreenSaverLayout.BubbleWidth;
            var apart = bubbleRight <= clockLeft - (ScreenSaverLayout.ClockGap - 0.001) || bubbleLeft >= clockRight + (ScreenSaverLayout.ClockGap - 0.001);
            Assert.True(apart, $"clock {clockLeft:0.000}-{clockRight:0.000}, bubble {bubbleLeft:0.000}-{bubbleRight:0.000}");
        }
    }

    [Fact]
    public void The_bubble_stays_on_the_screen()
    {
        foreach (var (_, bubble) in ThreeNights())
        {
            Assert.InRange(bubble.LeftPercent / 100, ScreenSaverLayout.EdgeGap - 0.001, 1 - ScreenSaverLayout.EdgeGap - ScreenSaverLayout.BubbleWidth + 0.001);
            Assert.InRange(bubble.TopPercent, ScreenSaverLayout.Margin, 100 - ScreenSaverLayout.Margin);
        }
    }

    [Fact]
    public void The_bubble_drifts_and_changes_side_during_a_night()
    {
        var bubbles = OneNightOfBubbles().ToList();
        var leftSide = bubbles.Count(b => b.Bubble.LeftPercent < ScreenSaverLayout.ClockSpan(b.Clock).Left * 100);

        Assert.InRange(leftSide, 1, bubbles.Count - 1);
        Assert.True(bubbles.Min(b => b.Bubble.TopPercent) < 20 && bubbles.Max(b => b.Bubble.TopPercent) > 80);
        Assert.Equal(ScreenSaverLayout.BubblePosition(Evening.AddSeconds(5)), ScreenSaverLayout.BubblePosition(Evening.AddSeconds(55)));
    }

    [Fact]
    public void The_bubble_moves_only_a_little_except_when_it_changes_side()
    {
        var bubbles = OneNightOfBubbles().ToList();

        foreach (var (before, after) in bubbles.Zip(bubbles.Skip(1)))
        {
            var sameSide = before.Bubble.LeftPercent < ScreenSaverLayout.ClockSpan(before.Clock).Left * 100
                == after.Bubble.LeftPercent < ScreenSaverLayout.ClockSpan(after.Clock).Left * 100;
            Assert.True(Math.Abs(after.Bubble.TopPercent - before.Bubble.TopPercent) <= 3, $"{before} -> {after}");
            if (sameSide)
            {
                Assert.True(Math.Abs(after.Bubble.LeftPercent - before.Bubble.LeftPercent) <= 3, $"{before} -> {after}");
            }
        }
    }

    private static IEnumerable<(ScreenSaverPosition Clock, ScreenSaverBubblePosition Bubble)> OneNightOfBubbles() =>
        ThreeNights().Take(12 * 60);
}
