namespace FamilyHub.Core.Devices;

/// <summary>
/// What the browser tells us about the screen and its input devices.
/// Detected once per connection (see <c>device.js</c>).
/// </summary>
public sealed record DeviceProfile
{
    /// <summary>Placeholder until the browser has reported in.</summary>
    public static DeviceProfile Unknown { get; } = new();

    /// <summary>False until detection has run.</summary>
    public bool IsKnown { get; init; }

    /// <summary><c>navigator.maxTouchPoints</c> – above 0 means a touch screen is present.</summary>
    public int MaxTouchPoints { get; init; }

    /// <summary>Primary pointer is a finger (<c>(pointer: coarse)</c>).</summary>
    public bool PrimaryPointerCoarse { get; init; }

    /// <summary>Any pointer is a finger (<c>(any-pointer: coarse)</c>).</summary>
    public bool AnyPointerCoarse { get; init; }

    /// <summary>A mouse or trackpad is present (<c>(any-pointer: fine)</c>).</summary>
    public bool AnyPointerFine { get; init; }

    /// <summary>Android/iOS/iPadOS – these have their own on-screen keyboard.</summary>
    public bool IsMobileOs { get; init; }

    public string Platform { get; init; } = "";

    public int ScreenWidth { get; init; }

    public int ScreenHeight { get; init; }

    public double PixelRatio { get; init; } = 1;

    public bool HasTouch => MaxTouchPoints > 0 || AnyPointerCoarse;

    /// <summary>Touch without mouse/trackpad – typically the kitchen screen.</summary>
    public bool IsTouchOnly => HasTouch && !AnyPointerFine;
}
