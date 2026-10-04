namespace FamilyHub.Core.Devices;

/// <summary>Where the clock sits on the screen saver, in percent of the screen (0 = left/top, 100 = right/bottom).</summary>
public readonly record struct ScreenSaverPosition(double LeftPercent, double TopPercent);

/// <summary>
/// Where the secondary bubble sits (e.g. the glucose in its ring): <see cref="LeftPercent"/> is its left edge in percent
/// of the screen width; <see cref="TopPercent"/> works like the clock's (the bubble is pulled up by the same percent of itself).
/// </summary>
public readonly record struct ScreenSaverBubblePosition(double LeftPercent, double TopPercent);

/// <summary>
/// Places the clock – and a smaller bubble beside it – on the screen saver. Both drift slowly along long, smooth
/// paths – a little every minute – so no part of the panel shows the same thing all night. Pure functions – see ScreenSaverTests.
/// </summary>
public static class ScreenSaverLayout
{
    /// <summary>The clock never comes closer to an edge than this (percent).</summary>
    public const double Margin = 15;

    /// <summary>
    /// Widths as a share of the screen width. Both are set in vw (tokens.css), so the shares hold on any screen:
    /// the time in --hub-text-clock (16vw) is about 0.42 wide, the ring 2.5 × --hub-saver-bubble (5.5vw) about 0.14.
    /// Rounded up, so the two never touch.
    /// </summary>
    public const double ClockWidth = 0.45;

    public const double BubbleWidth = 0.15;

    /// <summary>Air between the bubble and the screen's edge, and between the bubble and the clock (share of the width).</summary>
    public const double EdgeGap = 0.03;

    public const double ClockGap = 0.04;

    // Two different periods (minutes) give a Lissajous path that only repeats after days,
    // while each minute moves the clock just a few percent.
    private const double HorizontalPeriod = 113;
    private const double VerticalPeriod = 83;

    // The bubble's own, different periods – so it does not just follow the clock around.
    private const double BubbleHorizontalPeriod = 47;
    private const double BubbleVerticalPeriod = 97;

    public static ScreenSaverPosition ClockPosition(DateTimeOffset now)
    {
        var minute = Minute(now);
        const double reach = 50 - Margin;
        return new ScreenSaverPosition(
            Math.Round(50 + (reach * Math.Sin(2 * Math.PI * minute / HorizontalPeriod)), 1),
            Math.Round(50 + (reach * Math.Sin(2 * Math.PI * minute / VerticalPeriod)), 1));
    }

    /// <summary>
    /// The bubble keeps to the side of the clock with more room – there is always room, since the clock leaves at
    /// least (1 − <see cref="ClockWidth"/>) / 2 of the width free on one side. It changes side when the clock passes
    /// the middle of the screen, and drifts up and down on its own. It never overlaps the clock.
    /// </summary>
    public static ScreenSaverBubblePosition BubblePosition(DateTimeOffset now)
    {
        var (clockLeft, clockRight) = ClockSpan(ClockPosition(now));
        var (from, to) = clockLeft >= 1 - clockRight
            ? (EdgeGap, clockLeft - ClockGap - BubbleWidth)
            : (clockRight + ClockGap, 1 - EdgeGap - BubbleWidth);

        var minute = Minute(now);
        var left = from + ((to - from) * (0.5 + (0.5 * Math.Sin(2 * Math.PI * minute / BubbleHorizontalPeriod))));
        const double reach = 50 - Margin;
        var top = 50 + (reach * Math.Sin((2 * Math.PI * minute / BubbleVerticalPeriod) + Math.PI));
        return new ScreenSaverBubblePosition(Math.Round(left * 100, 1), Math.Round(top, 1));
    }

    /// <summary>The clock's left and right edge as a share of the width (it is pulled left by its own LeftPercent, see the CSS).</summary>
    public static (double Left, double Right) ClockSpan(ScreenSaverPosition clock)
    {
        var left = clock.LeftPercent / 100 * (1 - ClockWidth);
        return (left, left + ClockWidth);
    }

    private static double Minute(DateTimeOffset now) => Math.Floor(now.ToUnixTimeSeconds() / 60d);
}
