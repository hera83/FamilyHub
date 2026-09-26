using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers configuration, clock, storage, household settings and toasts.</summary>
    public static IServiceCollection AddFamilyHubCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FamilyHubOptions>()
            .Bind(configuration.GetSection(FamilyHubOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => TimeZoneResolver.TryFind(o.TimeZone, out _), "FamilyHub:TimeZone er ikke en kendt tidszone (brug fx 'Europe/Copenhagen').")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAppDataPaths, AppDataPaths>();

        // Keep ASP.NET's keys with the rest of the data, so they survive restarts
        // (the service user on the Raspberry Pi has no home folder).
        services.AddDataProtection().SetApplicationName("FamilyHub");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IAppDataPaths, ILoggerFactory>((options, paths, loggers) =>
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.GetDirectory("keys")), loggers));
        services.TryAddSingleton<IHubClock, HubClock>();
        services.TryAddSingleton<IHouseholdService, HouseholdService>();

        // Scoped = one per screen (Blazor circuit).
        services.TryAddScoped<IToastService, ToastService>();

        return services;
    }
}
