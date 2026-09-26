using FamilyHub.Core.Devices;

namespace FamilyHub.Tests.Core;

public class KeyboardPolicyTests
{
    private static readonly DeviceProfile KitchenScreen = new() { IsKnown = true, MaxTouchPoints = 10, AnyPointerCoarse = true, PrimaryPointerCoarse = true };
    private static readonly DeviceProfile TouchLaptop = new() { IsKnown = true, MaxTouchPoints = 10, AnyPointerCoarse = true, AnyPointerFine = true };
    private static readonly DeviceProfile PlainLaptop = new() { IsKnown = true, AnyPointerFine = true };
    private static readonly DeviceProfile Phone = new() { IsKnown = true, MaxTouchPoints = 5, AnyPointerCoarse = true, PrimaryPointerCoarse = true, IsMobileOs = true };

    [Fact]
    public void Auto_on_kitchen_screen_shows_keyboard_for_finger_taps()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Auto, KitchenScreen);

        Assert.True(result.Enabled);
        Assert.True(result.RequireTouchInteraction);
        Assert.Equal(KeyboardActivationReason.TouchScreenDetected, result.Reason);
    }

    [Fact]
    public void Auto_on_touch_laptop_requires_a_finger_tap_so_mouse_and_keyboard_are_left_alone()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Auto, TouchLaptop);

        Assert.True(result.Enabled);
        Assert.True(result.RequireTouchInteraction);
    }

    [Fact]
    public void Auto_without_touch_screen_is_off()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Auto, PlainLaptop);

        Assert.False(result.Enabled);
        Assert.Equal(KeyboardActivationReason.NoTouchScreen, result.Reason);
    }

    [Fact]
    public void Auto_on_phone_leaves_it_to_the_phones_own_keyboard()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Auto, Phone);

        Assert.False(result.Enabled);
        Assert.Equal(KeyboardActivationReason.DeviceHasOwnKeyboard, result.Reason);
    }

    [Fact]
    public void Auto_before_detection_is_off()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Auto, DeviceProfile.Unknown);

        Assert.False(result.Enabled);
        Assert.Equal(KeyboardActivationReason.DetectingDevice, result.Reason);
    }

    [Fact]
    public void Always_shows_keyboard_even_without_touch_and_without_requiring_a_tap()
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Always, PlainLaptop);

        Assert.True(result.Enabled);
        Assert.False(result.RequireTouchInteraction);
    }

    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Off_is_always_off(DeviceProfile profile)
    {
        var result = KeyboardPolicy.Resolve(KeyboardMode.Off, profile);

        Assert.False(result.Enabled);
        Assert.Equal(KeyboardActivationReason.DisabledByUser, result.Reason);
    }

    public static TheoryData<DeviceProfile> AllProfiles => [KitchenScreen, TouchLaptop, PlainLaptop, Phone, DeviceProfile.Unknown];

    [Fact]
    public void Touch_is_detected_from_touch_points_or_coarse_pointer()
    {
        Assert.True(new DeviceProfile { MaxTouchPoints = 1 }.HasTouch);
        Assert.True(new DeviceProfile { AnyPointerCoarse = true }.HasTouch);
        Assert.False(new DeviceProfile { AnyPointerFine = true }.HasTouch);
        Assert.True(KitchenScreen.IsTouchOnly);
        Assert.False(TouchLaptop.IsTouchOnly);
    }
}
