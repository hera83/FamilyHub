using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
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
    }
}
