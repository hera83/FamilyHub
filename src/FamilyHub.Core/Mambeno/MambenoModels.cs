namespace FamilyHub.Core.Mambeno;

/// <summary>A category in Mambeno. Categories form a tree: "Asiatisk" lies under "Aftensmad".</summary>
public sealed record MambenoCategory
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Recipes in the category. Some categories are empty (e.g. "Billigste opskrifter").</summary>
    public int RecipeCount { get; init; }

    /// <summary>The category above, or <c>null</c> for a top-level category.</summary>
    public int? ParentId { get; init; }

    public bool IsTopLevel => ParentId is null;
}

/// <summary>A category as it appears on a recipe (no count, no parent).</summary>
public sealed record MambenoCategoryRef(int Id, string Name);

/// <summary>One line in a recipe's ingredient list.</summary>
public sealed record MambenoIngredient
{
    /// <summary>The amount as written, e.g. "0.5". Empty for "salt og peber".</summary>
    public string Amount { get; init; } = "";

    /// <summary>The amount as a number, or <c>null</c> when there is none.</summary>
    public double? Quantity { get; init; }

    /// <summary>E.g. "gram", "spsk", "stk". May be empty.</summary>
    public string Unit { get; init; } = "";

    public required string Name { get; init; }

    /// <summary>The part of the dish the line belongs to, e.g. "Dressing" or "Tilbehør". <c>null</c> = no groups.</summary>
    public string? Group { get; init; }

    /// <summary>The Mambeno recipe for this ingredient, e.g. "færdige muffins" → the muffin recipe.</summary>
    public int? LinkedRecipeId { get; init; }
}

/// <summary>A recipe from Mambeno – read only.</summary>
public sealed record MambenoRecipe
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Mambeno's introduction to the dish. May be empty.</summary>
    public string Description { get; init; } = "";

    /// <summary>The main category's id, or <c>null</c> when the recipe has none.</summary>
    public int? CategoryId { get; init; }

    /// <summary>The main category's name. Empty = none.</summary>
    public string Category { get; init; } = "";

    /// <summary>Every category the recipe is in – often several ("Aftensmad", "Fisk", …).</summary>
    public IReadOnlyList<MambenoCategoryRef> Categories { get; init; } = [];

    public int PrepTimeMinutes { get; init; }

    public int CookTimeMinutes { get; init; }

    /// <summary>Kitchen time + waiting time. 0 = unknown, not "fast".</summary>
    public int TotalTimeMinutes { get; init; }

    /// <summary>Persons the amounts are for. 0 = unknown.</summary>
    public int Servings { get; init; }

    public string Author { get; init; } = "";

    /// <summary>Tips, often a job for the children. May be empty.</summary>
    public string Notes { get; init; } = "";

    /// <summary>When Mambeno last changed the recipe (UTC).</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>A public, absolute image address. Mambeno has none today.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>The recipe on mambeno.dk.</summary>
    public string? SourceUrl { get; init; }

    public IReadOnlyList<MambenoIngredient> Ingredients { get; init; } = [];

    public IReadOnlyList<string> Steps { get; init; } = [];

    public bool HasTime => TotalTimeMinutes > 0;

    public bool CanScale => Servings > 0;

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
}

/// <summary>Sort order for <see cref="MambenoQuery"/>.</summary>
public enum MambenoSort
{
    /// <summary>A–Å.</summary>
    Title,

    /// <summary>Newest change first.</summary>
    LastModified,

    /// <summary>Fastest first.</summary>
    TotalTime,
}

/// <summary>Filters for <see cref="IMambenoService.SearchRecipesAsync"/>. All filters are combined (AND).</summary>
public sealed record MambenoQuery
{
    public const int MaxPageSize = 200;

    /// <summary>Free text in title, description, category, notes or ingredient names. The API ignores case only for a–z, not æøå.</summary>
    public string? Search { get; init; }

    /// <summary>Exact category name.</summary>
    public string? Category { get; init; }

    public int? CategoryId { get; init; }

    /// <summary>Max total time. Recipes with unknown time (0) are left out.</summary>
    public int? MaxTotalMinutes { get; init; }

    /// <summary>Each text must be part of an ingredient name in the recipe.</summary>
    public IReadOnlyList<string> Ingredients { get; init; } = [];

    /// <summary>Only recipes changed at or after this time – for synchronisation.</summary>
    public DateTimeOffset? ModifiedSince { get; init; }

    public MambenoSort Sort { get; init; } = MambenoSort.Title;

    /// <summary>Starts at 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>1–200. Larger values are cut to 200.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>One page of search results.</summary>
public sealed record MambenoRecipePage
{
    public required IReadOnlyList<MambenoRecipe> Items { get; init; }

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages { get; init; }

    public bool HasMore => Page < TotalPages;
}

/// <summary>The API's own health check: is it up, and is its copy of Mambeno fresh?</summary>
public sealed record MambenoStatus
{
    /// <summary>True when the API says "Healthy" (it answers 503 when the data is older than 7 days or never synced).</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>The API's own word, e.g. "Healthy" or "Unhealthy".</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>When the API last fetched the recipes from Mambeno.</summary>
    public DateTimeOffset? DataLastUpdatedAt { get; init; }

    /// <summary>Why it is not healthy (English, from the API). For the log and the settings page.</summary>
    public string? Message { get; init; }
}
