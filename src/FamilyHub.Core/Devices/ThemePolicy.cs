namespace FamilyHub.Core.Devices;

/// <summary>Decides light or dark theme. Pure function – see ThemePolicyTests.</summary>
public static class ThemePolicy
{
    public static ResolvedTheme Resolve(DeviceSettings settings, TimeOnly now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Theme switch
        {
            ThemeMode.Light => ResolvedTheme.Light,
            ThemeMode.Dark => ResolvedTheme.Dark,
            _ => IsNight(now, settings.NightStartHour, settings.NightEndHour) ? ResolvedTheme.Dark : ResolvedTheme.Light,
        };
    }

    /// <summary>True if <paramref name="now"/> is inside the night window. Handles windows that wrap midnight.</summary>
    public static bool IsNight(TimeOnly now, int startHour, int endHour)
    {
        if (startHour == endHour)
        {
            return false;
        }

        var hour = now.Hour;
        return startHour < endHour
            ? hour >= startHour && hour < endHour
            : hour >= startHour || hour < endHour;
    }
}
