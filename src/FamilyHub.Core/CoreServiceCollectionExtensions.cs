using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Printing;
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
    /// <summary>Registers configuration, clock, storage, household settings, printing and toasts.</summary>
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

        // Printing for every menu: the family's print server (Print API). Empty address = not set up. See docs/printer.md.
        services.AddOptions<PrintApiOptions>()
            .Bind(configuration.GetSection(PrintApiOptions.SectionName))
            .Validate(o => string.IsNullOrWhiteSpace(o.BaseUrl) || o.TryGetBaseUri(out _),
                $"{PrintApiOptions.SectionName}:BaseUrl skal være tom eller en http(s)-adresse, fx http://homelab.local:8080.")
            .ValidateOnStart();
        services.AddHttpClient(PrintApiClient.HttpClientName);
        services.TryAddSingleton<PrintApiClient>();
        services.TryAddSingleton<PrintService>();
        services.AddHostedService<PrintJobWorker>();

        // Scoped = one per screen (Blazor circuit).
        services.TryAddScoped<IToastService, ToastService>();

        return services;
    }
}
