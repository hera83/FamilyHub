using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.UI.Components;

namespace FamilyHub.Modules.MealPlan.Components;

/// <summary>How dinners and recipes are shown: title, icon and the short facts line. Pure functions – unit-tested.</summary>
public static class DinnerLook
{
    /// <summary>The recipe's current title (it may have been renamed in the book), otherwise the one saved with the plan.</summary>
    public static string Title(PlannedDinner dinner, Recipe? recipe) => recipe?.Title ?? dinner.Title;

    /// <summary>Utensils for dinner recipes and the family's own dishes, an open book for other categories.</summary>
    public static IconName Icon(Recipe? recipe) =>
        recipe is null || !recipe.HasCategory || recipe.Category.Contains("aftensmad", StringComparison.OrdinalIgnoreCase)
            ? IconName.Utensils
            : IconName.BookOpen;

    /// <summary>"45 min" – null when the recipe book has no time (0 = not filled in).</summary>
    public static string? Time(Recipe recipe) =>
        recipe.TotalTimeMinutes > 0 ? DanishFormat.Duration(TimeSpan.FromMinutes(recipe.TotalTimeMinutes)) : null;

    /// <summary>"4 personer" – null when not filled in.</summary>
    public static string? Servings(Recipe recipe) => recipe.Servings switch
    {
        <= 0 => null,
        1 => "1 person",
        var n => $"{n} personer",
    };

    /// <summary>"45 min · Aftensmad" – the facts that exist, for lists and cards.</summary>
    public static string Summary(Recipe recipe) =>
        string.Join(" · ", new[] { Time(recipe), recipe.HasCategory ? recipe.Category : null }.OfType<string>());
}

/// <summary>A dinner dragged from one day to another.</summary>
public readonly record struct DinnerMove(DateOnly From, DateOnly To);
