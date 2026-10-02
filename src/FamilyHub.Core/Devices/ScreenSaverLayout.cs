namespace FamilyHub.Core.Devices;

/// <summary>Where the clock sits on the screen saver, in percent of the screen (0 = left/top, 100 = right/bottom).</summary>
public readonly record struct ScreenSaverPosition(double LeftPercent, double TopPercent);

/// <summary>
/// Places the clock on the screen saver. It drifts slowly along a long, smooth path – a little every minute –
/// so no part of the panel shows the same thing all night. Pure function – see ScreenSaverTests.
/// </summary>
public static class ScreenSaverLayout
{
    /// <summary>The clock never comes closer to an edge than this (percent).</summary>
    public const double Margin = 15;

    // Two different periods (minutes) give a Lissajous path that only repeats after days,
    // while each minute moves the clock just a few percent.
    private const double HorizontalPeriod = 113;
    private const double VerticalPeriod = 83;

    public static ScreenSaverPosition ClockPosition(DateTimeOffset now)
    {
        var minute = Math.Floor(now.ToUnixTimeSeconds() / 60d);
        const double reach = 50 - Margin;
        return new ScreenSaverPosition(
            Math.Round(50 + (reach * Math.Sin(2 * Math.PI * minute / HorizontalPeriod)), 1),
            Math.Round(50 + (reach * Math.Sin(2 * Math.PI * minute / VerticalPeriod)), 1));
    }
}
