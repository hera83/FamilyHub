namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// Where the family's recipe book ("Mors Opskrifter") lives – the <c>FamilyHub:MealPlan:Recipes</c> section.
/// The API key is a secret: <c>.env</c> on the server (<c>RECIPES_API_KEY</c>), <c>dotnet user-secrets</c> during development.
/// Never in appsettings.json in git.
/// </summary>
public sealed class RecipeApiOptions
{
    public const string SectionName = "FamilyHub:MealPlan:Recipes";

    /// <summary>The API root including the version, e.g. <c>https://opskriftsbog.ramskov.pro/api/v1</c>.</summary>
    public string BaseUrl { get; set; } = "https://opskriftsbog.ramskov.pro/api/v1";

    /// <summary>The shared key sent as <c>X-Api-Key</c>. Empty = not set up yet.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Seconds before a single call gives up (image uploads get four times as long).</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && TryGetBaseUri(out _);

    /// <summary>The base URL with a trailing slash, so relative paths ("recipes/5") are appended, not replaced.</summary>
    public bool TryGetBaseUri(out Uri baseUri)
    {
        var text = (BaseUrl ?? "").Trim();
        if (Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            baseUri = uri;
            return true;
        }

        baseUri = null!;
        return false;
    }
}
