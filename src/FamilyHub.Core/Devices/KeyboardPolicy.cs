namespace FamilyHub.Core.Devices;

/// <summary>Why the on-screen keyboard is (or is not) active on a screen.</summary>
public enum KeyboardActivationReason
{
    DisabledByUser,
    AlwaysOn,
    DetectingDevice,
    DeviceHasOwnKeyboard,
    NoTouchScreen,
    TouchScreenDetected,
}

/// <summary>The resolved keyboard behaviour for one screen.</summary>
/// <param name="Enabled">The keyboard may appear at all.</param>
/// <param name="RequireTouchInteraction">
/// Only appear when the field was tapped with a finger/pen. This is what keeps it away on a
/// laptop with a touch screen while you use the trackpad and physical keyboard.
/// </param>
public sealed record KeyboardActivation(bool Enabled, bool RequireTouchInteraction, KeyboardActivationReason Reason);

/// <summary>
/// Decides when the on-screen keyboard is offered. Pure function – see KeyboardPolicyTests.
/// </summary>
public static class KeyboardPolicy
{
    public static KeyboardActivation Resolve(KeyboardMode mode, DeviceProfile device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return mode switch
        {
            KeyboardMode.Off => new(false, false, KeyboardActivationReason.DisabledByUser),
            KeyboardMode.Always => new(true, false, KeyboardActivationReason.AlwaysOn),
            _ when !device.IsKnown => new(false, true, KeyboardActivationReason.DetectingDevice),
            _ when device.IsMobileOs => new(false, false, KeyboardActivationReason.DeviceHasOwnKeyboard),
            _ when !device.HasTouch => new(false, false, KeyboardActivationReason.NoTouchScreen),
            _ => new(true, true, KeyboardActivationReason.TouchScreenDetected),
        };
    }
}
