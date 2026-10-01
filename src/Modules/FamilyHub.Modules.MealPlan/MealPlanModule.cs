using FamilyHub.Core.Storage;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Print;
using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.Modules.MealPlan;

/// <summary>The week's meal plan and the shopping list generated from it.</summary>
public sealed class MealPlanModule : HubModule
{
    public const string Path = "/madplan";

    public override string Id => "madplan";

    public override string Title => "Madplan";

    public override string Description => "Ugens retter og indkøbsliste";

    public override IconName Icon => IconName.Utensils;

    public override string Route => Path;

    public override int Order => 20;

    /// <summary>"I aften" – today's dinner, from 06:00 until 19:00.</summary>
    public override IReadOnlyList<DashboardWidget> Widgets =>
        [DashboardWidget.For<TonightWidget>(WidgetSize.Medium, order: 20)];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // The family's recipe book (a separate app with an API). See docs/opskrifter-api.md.
        services.AddOptions<RecipeApiOptions>()
            .Bind(configuration.GetSection(RecipeApiOptions.SectionName))
            .Validate(o => o.TryGetBaseUri(out _), $"{RecipeApiOptions.SectionName}:BaseUrl skal være en http(s)-adresse, fx https://opskriftsbog.ramskov.pro/api/v1.")
            .ValidateOnStart();
        services.AddHttpClient(RecipeApiClient.HttpClientName);
        services.AddSingleton<RecipeApiClient>();
        services.AddSingleton<RecipeService>();
        services.AddHostedService<RecipeSyncWorker>();

        // "Print" on a recipe: PDF → the family's print server (PrintService in Core). See docs/printer.md.
        services.AddHttpClient(RecipePrinter.HttpClientName);
        services.AddSingleton<RecipePrinter>();

        // The family's plan: SQLite in the data folder (madplan/madplan.db).
        services.AddDbContextFactory<MealPlanDbContext>((provider, options) => options.UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = provider.GetRequiredService<IAppDataPaths>().GetFilePath("madplan/madplan.db") }.ToString()));
        services.AddSingleton<MealPlanService>();
    }
}
