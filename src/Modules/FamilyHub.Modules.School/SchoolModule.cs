using FamilyHub.Modules.School.Components;
using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Schedules;
using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.Modules.School;

/// <summary>The children's school timetables: shown on the kitchen screen, set up under the gear. See README.md.</summary>
public sealed class SchoolModule : HubModule
{
    public const string Path = "/skole";
    public const string SettingsPath = "/skole/indstillinger";
    public const string EditorRoute = "/skole/indstillinger/{Id:guid}";

    public static string EditorHref(Guid id) => $"skole/indstillinger/{id}";

    public override string Id => "skole";

    public override string Title => "Skole";

    public override string Description => "Børnenes skoleskemaer";

    public override IconName Icon => IconName.GraduationCap;

    public override string Route => Path;

    public override int Order => 20;

    /// <summary>"Skole i dag" / "Skole i morgen" – when each child has finished.</summary>
    public override IReadOnlyList<DashboardWidget> Widgets =>
        [DashboardWidget.For<SchoolWidget>(WidgetSize.Medium, order: 25)];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SchoolScheduleService>();

        // Optional calendar links for gymnasium and university (e.g. Moodle) – fetched every 30 minutes. See README.md.
        // The link holds a private key (in the query or, e.g. at Google, in the path) – so no request logging for it.
        services.AddHttpClient(ScheduleFeedService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize = ScheduleFeedService.MaxBytes;
            })
            .RemoveAllLoggers();
        services.AddSingleton<ScheduleFeedService>();
        services.AddHostedService<ScheduleFeedWorker>();
    }
}
