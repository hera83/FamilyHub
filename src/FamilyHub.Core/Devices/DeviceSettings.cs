namespace FamilyHub.Core.Devices;

/// <summary>When the on-screen keyboard is offered.</summary>
public enum KeyboardMode
{
    /// <summary>Shown when a text field is tapped with a finger on a touch screen (default).</summary>
    Auto,

    /// <summary>Shown whenever a text field gets focus, regardless of input device.</summary>
    Always,

    /// <summary>Never shown on this screen.</summary>
    Off,
}

public enum ThemeMode
{
    /// <summary>Light by day, dark during the night hours.</summary>
    Auto,
    Light,
    Dark,
}

public enum ResolvedTheme
{
    Light,
    Dark,
}

/// <summary>
/// Settings that belong to one physical screen (the kitchen display, a laptop, a phone).
/// Stored in that browser's localStorage – never shared between screens.
/// </summary>
public sealed record DeviceSettings
{
    public const int MinScalePercent = 80;
    public const int MaxScalePercent = 150;
    public const int MaxIdleMinutes = 120;

    public KeyboardMode KeyboardMode { get; init; } = KeyboardMode.Auto;

    public ThemeMode Theme { get; init; } = ThemeMode.Auto;

    /// <summary>Hour (0–23) where night mode starts when <see cref="Theme"/> is Auto.</summary>
    public int NightStartHour { get; init; } = 21;

    /// <summary>Hour (0–23) where night mode ends when <see cref="Theme"/> is Auto.</summary>
    public int NightEndHour { get; init; } = 6;

    /// <summary>Overall UI size in percent – everything is sized in rem, so it all scales together.</summary>
    public int DisplayScalePercent { get; init; } = 100;

    /// <summary>Return to the home screen after this many idle minutes. 0 = never.</summary>
    public int IdleReturnMinutes { get; init; }

    /// <summary>Darken the screen to a quiet clock after this many idle minutes. 0 = never.</summary>
    public int ScreenSaverMinutes { get; init; }

    /// <summary>Clamps values into valid ranges – stored data may be old or hand-edited.</summary>
    public DeviceSettings Normalize() => this with
    {
        KeyboardMode = Enum.IsDefined(KeyboardMode) ? KeyboardMode : KeyboardMode.Auto,
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeMode.Auto,
        NightStartHour = Math.Clamp(NightStartHour, 0, 23),
        NightEndHour = Math.Clamp(NightEndHour, 0, 23),
        DisplayScalePercent = Math.Clamp(DisplayScalePercent, MinScalePercent, MaxScalePercent),
        IdleReturnMinutes = Math.Clamp(IdleReturnMinutes, 0, MaxIdleMinutes),
        ScreenSaverMinutes = Math.Clamp(ScreenSaverMinutes, 0, MaxIdleMinutes),
    };
}
