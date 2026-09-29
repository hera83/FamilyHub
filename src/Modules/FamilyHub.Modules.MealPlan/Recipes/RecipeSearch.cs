using System.Globalization;
using FamilyHub.Core.Time;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// The API's search, done locally on the copy of the book – instant while typing and works without internet.
/// Same filters and sort orders as <c>GET /recipes</c>; text matching ignores case also for æ, ø and å.
/// </summary>
public static class RecipeSearch
{
    private static readonly CompareInfo Compare = DanishFormat.Culture.CompareInfo;
    private static readonly StringComparer TitleOrder = StringComparer.Create(DanishFormat.Culture, ignoreCase: true);

    /// <summary>Every recipe matching <paramref name="query"/>, sorted. Page and PageSize are ignored.</summary>
    public static IReadOnlyList<Recipe> Apply(IEnumerable<Recipe> recipes, RecipeQuery query)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(query);

        var search = query.Search?.Trim();
        var ingredients = query.Ingredients.Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        var matches = recipes.Where(r =>
            (string.IsNullOrEmpty(search)
                || Contains(r.Title, search) || Contains(r.Category, search) || Contains(r.Notes, search)
                || r.Ingredients.Any(i => Contains(i.Name, search)))
            && (string.IsNullOrWhiteSpace(query.Category) || string.Equals(r.Category, query.Category.Trim(), StringComparison.OrdinalIgnoreCase))
            && (query.CategoryId is not { } categoryId || r.CategoryId == categoryId)
            && (query.Difficulty is not { } difficulty || r.Difficulty == difficulty)
            && (query.MaxTotalMinutes is not { } max || r.TotalTimeMinutes <= max)
            && ingredients.All(wanted => r.Ingredients.Any(i => Contains(i.Name, wanted)))
            && (query.ModifiedSince is not { } since || r.LastModified >= since));

        return query.Sort switch
        {
            RecipeSort.LastModified => [.. matches.OrderByDescending(r => r.LastModified).ThenBy(r => r.Title, TitleOrder)],
            RecipeSort.TotalTime => [.. matches.OrderBy(r => r.TotalTimeMinutes).ThenBy(r => r.Title, TitleOrder)],
            _ => [.. matches.OrderBy(r => r.Title, TitleOrder)],
        };
    }

    private static bool Contains(string text, string part) =>
        Compare.IndexOf(text, part, CompareOptions.IgnoreCase) >= 0;
}
