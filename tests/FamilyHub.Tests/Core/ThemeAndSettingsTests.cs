using System.Text.Json;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Devices;
using FamilyHub.Core.Storage;
using Microsoft.Extensions.Configuration;

namespace FamilyHub.Tests.Core;

public class ThemeAndSettingsTests
{
    [Theory]
    [InlineData("20:59", false)]
    [InlineData("21:00", true)]
    [InlineData("23:30", true)]
    [InlineData("00:00", true)]
    [InlineData("05:59", true)]
    [InlineData("06:00", false)]
    [InlineData("12:00", false)]
    public void Night_window_wraps_around_midnight(string time, bool expectedNight)
    {
        Assert.Equal(expectedNight, ThemePolicy.IsNight(TimeOnly.Parse(time), startHour: 21, endHour: 6));
    }

    [Theory]
    [InlineData("00:59", false)]
    [InlineData("01:00", true)]
    [InlineData("04:59", true)]
    [InlineData("05:00", false)]
    public void Night_window_within_one_day(string time, bool expectedNight)
    {
        Assert.Equal(expectedNight, ThemePolicy.IsNight(TimeOnly.Parse(time), startHour: 1, endHour: 5));
    }

    [Fact]
    public void Same_start_and_end_means_no_night()
    {
        Assert.False(ThemePolicy.IsNight(new TimeOnly(22, 0), 22, 22));
    }

    [Fact]
    public void Explicit_theme_ignores_the_clock()
    {
        var midnight = new TimeOnly(0, 0);
        var noon = new TimeOnly(12, 0);

        Assert.Equal(ResolvedTheme.Light, ThemePolicy.Resolve(new DeviceSettings { Theme = ThemeMode.Light }, midnight));
        Assert.Equal(ResolvedTheme.Dark, ThemePolicy.Resolve(new DeviceSettings { Theme = ThemeMode.Dark }, noon));
        Assert.Equal(ResolvedTheme.Dark, ThemePolicy.Resolve(new DeviceSettings { Theme = ThemeMode.Auto }, midnight));
        Assert.Equal(ResolvedTheme.Light, ThemePolicy.Resolve(new DeviceSettings { Theme = ThemeMode.Auto }, noon));
    }

    [Fact]
    public void Normalize_clamps_stored_values_into_valid_ranges()
    {
        var messy = new DeviceSettings
        {
            KeyboardMode = (KeyboardMode)42,
            Theme = (ThemeMode)(-1),
            NightStartHour = 30,
            NightEndHour = -4,
            DisplayScalePercent = 500,
            IdleReturnMinutes = -10,
            ScreenSaverMinutes = 999,
        };

        var clean = messy.Normalize();

        Assert.Equal(KeyboardMode.Auto, clean.KeyboardMode);
        Assert.Equal(ThemeMode.Auto, clean.Theme);
        Assert.Equal(23, clean.NightStartHour);
        Assert.Equal(0, clean.NightEndHour);
        Assert.Equal(DeviceSettings.MaxScalePercent, clean.DisplayScalePercent);
        Assert.Equal(0, clean.IdleReturnMinutes);
        Assert.Equal(DeviceSettings.MaxIdleMinutes, clean.ScreenSaverMinutes);
    }

    [Fact]
    public void Screen_saver_is_off_unless_chosen_and_survives_json()
    {
        Assert.Equal(0, new DeviceSettings().ScreenSaverMinutes);

        var json = JsonSerializer.Serialize(new DeviceSettings { ScreenSaverMinutes = 15 }, HubJson.Compact);
        var back = JsonSerializer.Deserialize<DeviceSettings>(json, HubJson.Compact)!;

        Assert.Equal(15, back.ScreenSaverMinutes);
    }

    [Fact]
    public void Settings_saved_before_the_screen_saver_existed_still_load_with_it_off()
    {
        var old = """{"keyboardMode":"Auto","theme":"Dark","idleReturnMinutes":5}""";

        var settings = JsonSerializer.Deserialize<DeviceSettings>(old, HubJson.Compact)!.Normalize();

        Assert.Equal(5, settings.IdleReturnMinutes);
        Assert.Equal(0, settings.ScreenSaverMinutes);
    }

    [Fact]
    public void Settings_round_trip_through_json_with_readable_enums()
    {
        var settings = new DeviceSettings { KeyboardMode = KeyboardMode.Always, Theme = ThemeMode.Dark, DisplayScalePercent = 115 };

        var json = JsonSerializer.Serialize(settings, HubJson.Compact);
        var back = JsonSerializer.Deserialize<DeviceSettings>(json, HubJson.Compact);

        Assert.Contains("\"keyboardMode\":\"Always\"", json);
        Assert.Equal(settings, back);
    }

    [Fact]
    public void Device_defaults_bind_from_appsettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FamilyHub:TimeZone"] = "Europe/Copenhagen",
                ["FamilyHub:DeviceDefaults:KeyboardMode"] = "Always",
                ["FamilyHub:DeviceDefaults:NightStartHour"] = "22",
                ["FamilyHub:DeviceDefaults:IdleReturnMinutes"] = "5",
            })
            .Build();

        var options = configuration.GetSection(FamilyHubOptions.SectionName).Get<FamilyHubOptions>()!;

        Assert.Equal(KeyboardMode.Always, options.DeviceDefaults.KeyboardMode);
        Assert.Equal(22, options.DeviceDefaults.NightStartHour);
        Assert.Equal(5, options.DeviceDefaults.IdleReturnMinutes);
        Assert.Equal(ThemeMode.Auto, options.DeviceDefaults.Theme);
    }
}
