using FamilyHub.UI.Keyboard;
using FamilyHub.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyHub.UI;

public static class UIServiceCollectionExtensions
{
    /// <summary>Registers the per-screen UI services: device, on-screen keyboard and dialogs.</summary>
    public static IServiceCollection AddFamilyHubUI(this IServiceCollection services)
    {
        // Scoped = one per screen (Blazor circuit).
        services.TryAddScoped<DeviceService>();
        services.TryAddScoped<KeyboardService>();
        services.TryAddScoped<DialogService>();
        return services;
    }
}
