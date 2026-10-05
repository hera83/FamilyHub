using FamilyHub.Core.Mambeno;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Plan;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// "Inspiration": recipes from Mambeno, copied into the family's recipe book when chosen, so they can be edited
/// like the family's own. Pure functions – unit-tested. See docs/mambeno.md.
/// </summary>
public static class Inspiration
{
    /// <summary>Written as the recipe's author, so the family can see where it came from.</summary>
    public const string Author = "Mambeno";

    /// <summary>The recipe book's copy of a Mambeno recipe: same title (case and spacing ignored). Null = not copied yet.</summary>
    public static Recipe? FindInBook(MambenoRecipe recipe, IEnumerable<Recipe> book)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var title = TitleKey(recipe.Title);
        return book.FirstOrDefault(r => TitleKey(r.Title) == title);
    }

    /// <summary>The Mambeno category a course starts on: main course → "Aftensmad", dessert → "Kager, bagværk og sødt", starter → all.</summary>
    public static int? CategoryFor(DinnerCourse course, IEnumerable<MambenoCategory> categories)
    {
        var word = course switch
        {
            DinnerCourse.Main => "aftensmad",
            DinnerCourse.Dessert => "sødt",
            _ => null,
        };

        return word is null
            ? null
            : categories.FirstOrDefault(c => c.IsTopLevel && c.RecipeCount > 0 && c.Name.Contains(word, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// The recipe as a draft for the recipe book. The category is one the book already has – one of the recipe's own
    /// Mambeno categories ("Aftensmad") or the course's ("Dessert") – so the book never fills up with Mambeno's 47 categories.
    /// Groups ("Dressing") become sub-headings ("Dressing:"), the way the book writes them.
    /// </summary>
    public static RecipeDraft ToDraft(MambenoRecipe recipe, IEnumerable<RecipeCategory> bookCategories, DinnerCourse course)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var categories = bookCategories.ToList();
        var names = new[] { recipe.Category }.Concat(recipe.Categories.Select(c => c.Name));
        var categoryId = names
            .Select(name => categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id)
            .FirstOrDefault(id => id is not null)
            ?? MealPlanRules.CategoryFor(course, categories);

        return new RecipeDraft
        {
            Title = Cut(recipe.Title, RecipeDraft.MaxTitleLength),
            CategoryId = categoryId,
            PrepTimeMinutes = Math.Clamp(recipe.PrepTimeMinutes, 0, RecipeDraft.MaxMinutes),
            CookTimeMinutes = Math.Clamp(recipe.CookTimeMinutes, 0, RecipeDraft.MaxMinutes),
            Servings = Math.Clamp(recipe.Servings, 0, RecipeDraft.MaxServings),
            Author = Author,
            Notes = Notes(recipe),
            Ingredients = Ingredients(recipe.Ingredients),
            Steps = [.. recipe.Steps],
        };
    }

    /// <summary>"35 min · 4 personer · Fisk" – the facts that exist, for the list.</summary>
    public static string Summary(MambenoRecipe recipe) =>
        string.Join(" · ", new[] { Time(recipe), Servings(recipe), recipe.Category.Length > 0 ? recipe.Category : null }.OfType<string>());

    /// <summary>"45 min" – null when Mambeno has no time.</summary>
    public static string? Time(MambenoRecipe recipe) =>
        recipe.HasTime ? DanishFormat.Duration(TimeSpan.FromMinutes(recipe.TotalTimeMinutes)) : null;

    /// <summary>"4 personer" – null when unknown.</summary>
    public static string? Servings(MambenoRecipe recipe) => recipe.Servings switch
    {
        <= 0 => null,
        1 => "1 person",
        var n => $"{n} personer",
    };

    /// <summary>"½ spsk paprika" – the amount the Danish way.</summary>
    public static string LineText(MambenoIngredient ingredient)
    {
        var amount = ingredient.Quantity is { } quantity and > 0 ? RecipeMath.FormatQuantity(quantity) : ingredient.Amount;
        return string.Join(' ', new[] { amount, ingredient.Unit, ingredient.Name }.Where(p => p.Length > 0));
    }

    private static IReadOnlyList<IngredientDraft> Ingredients(IReadOnlyList<MambenoIngredient> ingredients)
    {
        var lines = new List<IngredientDraft>();
        var grouped = ingredients.Any(i => i.Group is not null);
        string? group = null;
        foreach (var ingredient in ingredients)
        {
            if (grouped && ingredient.Group is { } name && name != group)
            {
                lines.Add(new IngredientDraft { Name = Cut(name.TrimEnd(':'), IngredientDraft.MaxNameLength - 1) + ":" });
            }

            group = ingredient.Group;
            lines.Add(new IngredientDraft
            {
                // The book reads both "0.5" and "0,5"; the comma is how the family writes it.
                Amount = Cut(ingredient.Amount.Replace('.', ','), IngredientDraft.MaxAmountLength),
                Unit = Cut(ingredient.Unit, IngredientDraft.MaxUnitLength),
                Name = Cut(ingredient.Name, IngredientDraft.MaxNameLength),
            });
        }

        return lines;
    }

    private static string Notes(MambenoRecipe recipe)
    {
        var parts = new List<string>();
        if (recipe.Description.Length > 0)
        {
            parts.Add(recipe.Description);
        }

        if (recipe.Notes.Length > 0)
        {
            parts.Add(recipe.Notes);
        }

        parts.Add(recipe.SourceUrl is { } url ? $"Fra Mambeno: {url}" : "Fra Mambeno.");
        return string.Join("\n\n", parts);
    }

    private static string TitleKey(string title) =>
        string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLower(DanishFormat.Culture);

    private static string Cut(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }
}
