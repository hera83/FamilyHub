using System.Text.Json;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Devices;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace FamilyHub.UI.Services;

/// <summary>
/// The screen the user is standing at: what it can do (touch?) and its own settings
/// (keyboard, theme, size). One instance per screen/connection.
/// </summary>
public sealed class DeviceService(
    IJSRuntime js,
    IHubClock clock,
    IOptions<FamilyHubOptions> options,
    ILogger<DeviceService> logger) : IAsyncDisposable
{
    internal const string SettingsKey = "familyhub.device.v1";

    private IJSObjectReference? module;
    private Task? initialization;
    private string? appliedAppearance;

    public DeviceProfile Profile { get; private set; } = DeviceProfile.Unknown;

    public DeviceSettings Settings { get; private set; } = (options.Value.DeviceDefaults ?? new DeviceSettings()).Normalize();

    public ResolvedTheme Theme { get; private set; }

    public bool IsInitialized { get; private set; }

    /// <summary>The resolved on-screen keyboard behaviour for this screen.</summary>
    public KeyboardActivation Keyboard => KeyboardPolicy.Resolve(Settings.KeyboardMode, Profile);

    /// <summary>Raised when the profile or the settings change. Raised on the circuit's thread.</summary>
    public event Action? Changed;

    /// <summary>Detects the device and loads its settings. Call from OnAfterRenderAsync; safe to call repeatedly.</summary>
    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    public async Task UpdateSettingsAsync(Func<DeviceSettings, DeviceSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        var updated = update(Settings).Normalize();
        if (updated == Settings)
        {
            return;
        }

        Settings = updated;
        if (module is not null)
        {
            await module.InvokeVoidAsync("saveSettings", SettingsKey, JsonSerializer.Serialize(updated, HubJson.Compact));
        }

        await ApplyAppearanceAsync();
        RaiseChanged();
    }

    /// <summary>Re-evaluates the time-based theme. The layout calls this every minute.</summary>
    public async Task RefreshAppearanceAsync()
    {
        if (!IsInitialized)
        {
            return;
        }

        try
        {
            if (await ApplyAppearanceAsync())
            {
                RaiseChanged();
            }
        }
        catch (JSDisconnectedException)
        {
            // The screen went away – nothing to update.
        }
    }

    /// <summary>Shows the screen saver right away (the "Prøv" button). A touch wakes the screen again.</summary>
    public async Task ShowScreenSaverAsync()
    {
        if (module is not null)
        {
            await module.InvokeVoidAsync("showScreenSaver");
        }
    }

    /// <summary>Reloads the page in the browser (e.g. after an update).</summary>
    public async Task ReloadScreenAsync()
    {
        if (module is not null)
        {
            await module.InvokeVoidAsync("reload");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task InitializeCoreAsync()
    {
        module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/FamilyHub.UI/js/device.js");
        var snapshot = await module.InvokeAsync<DeviceSnapshot>("initialize", SettingsKey);

        Profile = (snapshot.Profile ?? DeviceProfile.Unknown) with { IsKnown = true };
        if (TryReadSettings(snapshot.Settings) is { } stored)
        {
            Settings = stored.Normalize();
        }

        IsInitialized = true;
        await ApplyAppearanceAsync();
        logger.LogInformation(
            "Screen connected: touch={Touch} ({Points} points), mobile={Mobile}, {Width}x{Height}, keyboard={Keyboard}",
            Profile.HasTouch, Profile.MaxTouchPoints, Profile.IsMobileOs, Profile.ScreenWidth, Profile.ScreenHeight, Keyboard.Reason);
        RaiseChanged();
    }

    private DeviceSettings? TryReadSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceSettings>(json, HubJson.Compact);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Stored screen settings were unreadable - using defaults");
            return null;
        }
    }

    /// <returns>True if the theme or scale actually changed.</returns>
    private async Task<bool> ApplyAppearanceAsync()
    {
        Theme = ThemePolicy.Resolve(Settings, TimeOnly.FromDateTime(clock.Now.DateTime));
        var theme = Theme == ResolvedTheme.Dark ? "dark" : "light";
        var appearance = $"{theme}:{Settings.DisplayScalePercent}";
        if (appearance == appliedAppearance || module is null)
        {
            return false;
        }

        await module.InvokeVoidAsync("applyAppearance", theme, Settings.DisplayScalePercent);
        appliedAppearance = appearance;
        return true;
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Device change subscriber failed");
        }
    }

    private sealed class DeviceSnapshot
    {
        public DeviceProfile? Profile { get; init; }

        public string? Settings { get; init; }
    }
}
