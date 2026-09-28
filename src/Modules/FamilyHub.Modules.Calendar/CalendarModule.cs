using FamilyHub.Modules.Calendar.Components;
using FamilyHub.Modules.Calendar.Google;
using FamilyHub.Modules.Calendar.Services;
using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.Modules.Calendar;

/// <summary>The family calendar – the family's Google calendars on the kitchen screen.</summary>
public sealed class CalendarModule : HubModule
{
    public const string Path = "/kalender";
    public const string SettingsPath = "/kalender/indstillinger";
    public const string CallbackPath = "/kalender/google/callback";

    public override string Id => "kalender";

    public override string Title => "Kalender";

    public override string Description => "Familiens fælles kalender";

    public override IconName Icon => IconName.Calendar;

    public override string Route => Path;

    public override int Order => 10;

    public override IReadOnlyList<DashboardWidget> Widgets =>
        [DashboardWidget.For<TodayWidget>(WidgetSize.Medium, order: 10)];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(GoogleOAuthClient.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<GoogleCredentialsProvider>();
        services.AddSingleton<GoogleOAuthClient>();
        services.AddSingleton<GoogleCalendarApi>();
        services.AddSingleton<CalendarService>();
        services.AddHostedService<CalendarSyncWorker>();
    }
}
