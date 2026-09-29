using System.Text.Json.Serialization;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>A recipe from the recipe book – with ingredients and method. See docs/opskrifter-api.md.</summary>
public sealed record Recipe
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Null when the recipe has no category (or its category no longer exists).</summary>
    public int? CategoryId { get; init; }

    /// <summary>The category's name – empty when the recipe has none.</summary>
    public string Category { get; init; } = "";

    /// <summary>Material Symbols name from the recipe book, e.g. "cookie". Map it to a Family Hub icon in the UI.</summary>
    public string CategoryIcon { get; init; } = "";

    public int PrepTimeMinutes { get; init; }

    public int CookTimeMinutes { get; init; }

    /// <summary>Prep + cook. 0 = not filled in (common in the book) – show nothing rather than "0 min".</summary>
    public int TotalTimeMinutes { get; init; }

    /// <summary>People the recipe is written for. 0 = not filled in – then the recipe cannot be scaled.</summary>
    public int Servings { get; init; }

    public RecipeDifficulty? Difficulty { get; init; }

    public string Author { get; init; } = "";

    public string Notes { get; init; } = "";

    /// <summary>When the recipe was last changed (the API sends UTC without an offset).</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>Absolute, public URL of the photo (no API key needed – usable directly in <c>&lt;img&gt;</c>). Null = no photo.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>In the order of the recipe. Lines with <see cref="RecipeIngredient.IsHeading"/> are sub-headings ("Glasur:").</summary>
    public IReadOnlyList<RecipeIngredient> Ingredients { get; init; } = [];

    /// <summary>The method, one step per entry. A step may contain line breaks.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    [JsonIgnore]
    public bool HasCategory => Category.Length > 0;

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrEmpty(ImageUrl);

    [JsonIgnore]
    public bool CanScale => Servings > 0;
}

/// <summary>One ingredient line, e.g. "2 dl mælk".</summary>
public sealed record RecipeIngredient
{
    /// <summary>The amount exactly as written ("2", "1½", "2-3", "1,5") – may be empty.</summary>
    public string Amount { get; init; } = "";

    /// <summary>The amount as a number when it can be read ("1½" → 1.5), otherwise null ("2-3", ""). Used for scaling and shopping lists.</summary>
    public double? Quantity { get; init; }

    public string Unit { get; init; } = "";

    public required string Name { get; init; }

    /// <summary>
    /// A sub-heading inside the ingredient list, e.g. "Glasur:" – no amount, no unit, ends with a colon.
    /// Show it as a heading and leave it out of shopping lists.
    /// </summary>
    [JsonIgnore]
    public bool IsHeading => Amount.Length == 0 && Unit.Length == 0 && Name.EndsWith(':');
}

/// <summary>A category and how many recipes are in it.</summary>
public sealed record RecipeCategory
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Material Symbols name, e.g. "cookie" (the recipe book's default).</summary>
    public string Icon { get; init; } = RecipeCategoryDraft.DefaultIcon;

    public int RecipeCount { get; init; }
}

/// <summary>An ingredient name used in the book and in how many recipes – good for suggestions and "what can we make with …".</summary>
public sealed record IngredientUsage(string Name, int RecipeCount);

/// <summary>Everything needed to fill pickers, in one call (<c>GET /lookups</c>).</summary>
public sealed record RecipeLookups
{
    public static RecipeLookups Empty { get; } = new();

    public IReadOnlyList<RecipeDifficulty> Difficulties { get; init; } = [RecipeDifficulty.Easy, RecipeDifficulty.Medium, RecipeDifficulty.Hard];

    public IReadOnlyList<RecipeCategory> Categories { get; init; } = [];

    /// <summary>Units already used in the book ("dl", "gram", "spsk" …).</summary>
    public IReadOnlyList<string> Units { get; init; } = [];

    public IReadOnlyList<IngredientUsage> Ingredients { get; init; } = [];
}

/// <summary>One page of search results.</summary>
public sealed record RecipePage
{
    public IReadOnlyList<Recipe> Items { get; init; } = [];

    public int TotalCount { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; }

    public int TotalPages { get; init; }

    [JsonIgnore]
    public bool HasMore => Page < TotalPages;
}

/// <summary>The recipe book's difficulty levels. In JSON and in the UI: "Let", "Middel", "Svær".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecipeDifficulty>))]
public enum RecipeDifficulty
{
    [JsonStringEnumMemberName("Let")]
    Easy,

    [JsonStringEnumMemberName("Middel")]
    Medium,

    [JsonStringEnumMemberName("Svær")]
    Hard,
}

public static class RecipeDifficultyExtensions
{
    /// <summary>The Danish word – also the value the API expects.</summary>
    public static string Label(this RecipeDifficulty difficulty) => difficulty switch
    {
        RecipeDifficulty.Easy => "Let",
        RecipeDifficulty.Medium => "Middel",
        RecipeDifficulty.Hard => "Svær",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    public static RecipeDifficulty? ParseDifficulty(string? text) => text?.Trim() switch
    {
        "Let" => RecipeDifficulty.Easy,
        "Middel" => RecipeDifficulty.Medium,
        "Svær" => RecipeDifficulty.Hard,
        _ => null,
    };
}

/// <summary>Sort order for recipe searches.</summary>
public enum RecipeSort
{
    /// <summary>A–Å.</summary>
    Title,

    /// <summary>Newest change first.</summary>
    LastModified,

    /// <summary>Quickest first (recipes without a time count as 0 and come first).</summary>
    TotalTime,
}

/// <summary>
/// Filters for a recipe search. All filters combine (AND). Used both online
/// (<see cref="RecipeApiClient.SearchRecipesAsync"/>) and offline on the local copy (<see cref="RecipeService.Search"/>).
/// </summary>
public sealed record RecipeQuery
{
    public const int MaxPageSize = 200;

    /// <summary>Free text in title, category, notes or ingredient names.</summary>
    public string? Search { get; init; }

    /// <summary>Exact category name. <see cref="CategoryId"/> is usually the better choice.</summary>
    public string? Category { get; init; }

    public int? CategoryId { get; init; }

    public RecipeDifficulty? Difficulty { get; init; }

    /// <summary>Max prep + cook in minutes.</summary>
    public int? MaxTotalMinutes { get; init; }

    /// <summary>Each text must be part of an ingredient name in the recipe ("mel" matches "Hvedemel").</summary>
    public IReadOnlyList<string> Ingredients { get; init; } = [];

    /// <summary>Only recipes changed at or after this time.</summary>
    public DateTimeOffset? ModifiedSince { get; init; }

    public RecipeSort Sort { get; init; } = RecipeSort.Title;

    /// <summary>1-based. Ignored by the local search, which returns every match.</summary>
    public int Page { get; init; } = 1;

    /// <summary>1–200. Ignored by the local search.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>The data for creating or replacing a recipe (<c>RecipeInput</c>). Check it with <see cref="RecipeValidation"/> before sending.</summary>
public sealed record RecipeDraft
{
    public const int MaxTitleLength = 200;
    public const int MaxCategoryLength = 100;
    public const int MaxIconLength = 50;
    public const int MaxAuthorLength = 100;
    public const int MaxMinutes = 100_000;
    public const int MaxServings = 1000;

    public required string Title { get; init; }

    /// <summary>An existing category. Wins over <see cref="Category"/>.</summary>
    public int? CategoryId { get; init; }

    /// <summary>A category name – created automatically if it does not exist.</summary>
    public string? Category { get; init; }

    /// <summary>Icon for a category created by <see cref="Category"/>. Ignored if the category exists.</summary>
    public string? CategoryIcon { get; init; }

    public int PrepTimeMinutes { get; init; }

    public int CookTimeMinutes { get; init; }

    public int Servings { get; init; } = 4;

    public RecipeDifficulty? Difficulty { get; init; }

    /// <summary>Null: "Mor" when creating, unchanged when replacing.</summary>
    public string? Author { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<IngredientDraft> Ingredients { get; init; } = [];

    /// <summary>Empty steps are skipped by the API.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>A draft with everything from an existing recipe – the starting point for editing (and for "Fortryd" after a delete).</summary>
    public static RecipeDraft From(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new RecipeDraft
        {
            Title = recipe.Title,
            CategoryId = recipe.CategoryId,
            Category = recipe.CategoryId is null && recipe.HasCategory ? recipe.Category : null,
            CategoryIcon = recipe.CategoryId is null && recipe.HasCategory ? recipe.CategoryIcon : null,
            PrepTimeMinutes = recipe.PrepTimeMinutes,
            CookTimeMinutes = recipe.CookTimeMinutes,
            Servings = recipe.Servings,
            Difficulty = recipe.Difficulty,
            Author = recipe.Author,
            Notes = recipe.Notes,
            Ingredients = [.. recipe.Ingredients.Select(i => new IngredientDraft { Amount = i.Amount, Unit = i.Unit, Name = i.Name })],
            Steps = [.. recipe.Steps],
        };
    }
}

/// <summary>One ingredient line in a <see cref="RecipeDraft"/>.</summary>
public sealed record IngredientDraft
{
    public const int MaxAmountLength = 50;
    public const int MaxUnitLength = 50;
    public const int MaxNameLength = 200;

    /// <summary>Free text: "2", "1½", "2-3". The API works out <see cref="RecipeIngredient.Quantity"/> itself.</summary>
    public string? Amount { get; init; }

    public string? Unit { get; init; }

    public required string Name { get; init; }
}

/// <summary>The data for creating or renaming a category (<c>CategoryInput</c>).</summary>
public sealed record RecipeCategoryDraft
{
    public const string DefaultIcon = "cookie";
    public const int MaxNameLength = 100;
    public const int MaxIconLength = 50;

    public required string Name { get; init; }

    /// <summary>Material Symbols name. Null: "cookie" when creating, unchanged when renaming.</summary>
    public string? Icon { get; init; }
}
