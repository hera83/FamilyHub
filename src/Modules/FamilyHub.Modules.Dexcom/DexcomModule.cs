using FamilyHub.Modules.Dexcom.Components;
using FamilyHub.Modules.Dexcom.Glucose;
using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.Modules.Dexcom;

/// <summary>Glucose from Dexcom: the reading right now, a graph of the last hours and time in range. See README.md.</summary>
public sealed class DexcomModule : HubModule
{
    public const string Path = "/dexcom";
    public const string SettingsPath = "/dexcom/indstillinger";

    public override string Id => "dexcom";

    public override string Title => "Dexcom";

    public override string Description => "Blodsukker lige nu og over tid";

    public override IconName Icon => IconName.Droplet;

    public override string Route => Path;

    public override int Order => 30;

    /// <summary>"Blodsukker" – the reading right now, only while it is current (8 minutes).</summary>
    public override IReadOnlyList<DashboardWidget> Widgets =>
        [DashboardWidget.For<GlucoseWidget>(WidgetSize.Medium, order: 30)];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The API client (IDexcomService) lives in Core – see docs/dexcom.md. One copy of the readings for every screen.
        services.AddSingleton<GlucoseMonitor>();
        services.AddHostedService<GlucoseSyncWorker>();
    }
}
