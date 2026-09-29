namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// The recipe book's rules, checked before sending – so a mistake shows under the field (its <c>Error</c>)
/// instead of as a failed call. Keys match the API's field names, e.g. "Title" or "Ingredients[2].Name".
/// </summary>
public static class RecipeValidation
{
    /// <summary>The problems with a recipe draft. Empty = OK to send.</summary>
    public static IReadOnlyDictionary<string, string> Validate(RecipeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new Dictionary<string, string>();

        Required(errors, "Title", draft.Title, RecipeDraft.MaxTitleLength, "Skriv et navn på retten.");
        MaxLength(errors, "Category", draft.Category, RecipeDraft.MaxCategoryLength);
        MaxLength(errors, "CategoryIcon", draft.CategoryIcon, RecipeDraft.MaxIconLength);
        MaxLength(errors, "Author", draft.Author, RecipeDraft.MaxAuthorLength);
        Range(errors, "PrepTimeMinutes", draft.PrepTimeMinutes, RecipeDraft.MaxMinutes);
        Range(errors, "CookTimeMinutes", draft.CookTimeMinutes, RecipeDraft.MaxMinutes);
        Range(errors, "Servings", draft.Servings, RecipeDraft.MaxServings);

        for (var i = 0; i < draft.Ingredients.Count; i++)
        {
            var ingredient = draft.Ingredients[i];
            Required(errors, $"Ingredients[{i}].Name", ingredient.Name, IngredientDraft.MaxNameLength, "Skriv hvad ingrediensen er.");
            MaxLength(errors, $"Ingredients[{i}].Amount", ingredient.Amount, IngredientDraft.MaxAmountLength);
            MaxLength(errors, $"Ingredients[{i}].Unit", ingredient.Unit, IngredientDraft.MaxUnitLength);
        }

        return errors;
    }

    /// <summary>The problems with a category draft. Empty = OK to send.</summary>
    public static IReadOnlyDictionary<string, string> Validate(RecipeCategoryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new Dictionary<string, string>();
        Required(errors, "Name", draft.Name, RecipeCategoryDraft.MaxNameLength, "Skriv et navn på kategorien.");
        MaxLength(errors, "Icon", draft.Icon, RecipeCategoryDraft.MaxIconLength);
        return errors;
    }

    private static void Required(Dictionary<string, string> errors, string field, string? value, int max, string missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = missing;
            return;
        }

        MaxLength(errors, field, value, max);
    }

    private static void MaxLength(Dictionary<string, string> errors, string field, string? value, int max)
    {
        if (value is not null && value.Trim().Length > max)
        {
            errors[field] = $"Højst {max} tegn.";
        }
    }

    private static void Range(Dictionary<string, string> errors, string field, int value, int max)
    {
        if (value < 0 || value > max)
        {
            errors[field] = $"Skal være mellem 0 og {max}.";
        }
    }
}
