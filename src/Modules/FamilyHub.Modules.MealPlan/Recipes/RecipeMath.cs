using System.Globalization;
using FamilyHub.Core.Time;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>An ingredient line adjusted to a number of people – ready to show ("1½ dl mælk").</summary>
public sealed record ScaledIngredient
{
    public required string Name { get; init; }

    public string Unit { get; init; } = "";

    /// <summary>Scaled quantity, or null when the amount is not a number ("2-3", "en knivspids").</summary>
    public double? Quantity { get; init; }

    /// <summary>What to show: the scaled number in Danish ("1½", "0,3", "250"), or the original text when it cannot be scaled.</summary>
    public string AmountText { get; init; } = "";

    public bool IsHeading { get; init; }

    /// <summary>"1½ dl mælk" – the whole line.</summary>
    public string Text => string.Join(' ', new[] { AmountText, Unit, Name }.Where(p => p.Length > 0));
}

/// <summary>One line on a combined shopping list.</summary>
public sealed record ShoppingItem
{
    /// <summary>The name as first written, e.g. "Hvedemel".</summary>
    public required string Name { get; init; }

    public string Unit { get; init; } = "";

    /// <summary>The summed quantity, or null when at least one of the amounts was not a number.</summary>
    public double? Quantity { get; init; }

    /// <summary>"900" / "1½" – or the original amounts joined ("2-3 + 1") when they could not be summed. Empty = no amount.</summary>
    public string AmountText { get; init; } = "";

    /// <summary>The recipes the item comes from (ids), for "bruges i …".</summary>
    public IReadOnlyList<int> RecipeIds { get; init; } = [];

    public string Text => string.Join(' ', new[] { AmountText, Unit, Name }.Where(p => p.Length > 0));
}

/// <summary>
/// Portions, amounts and shopping lists. Pure functions – no API calls, unit-tested.
/// Amounts are shown the Danish way: halves and quarters as ½ ¼ ¾, otherwise a decimal comma.
/// </summary>
public static class RecipeMath
{
    private static readonly Dictionary<string, string> UnitSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = "gram",
        ["gr"] = "gram",
        ["gr."] = "gram",
        ["spiseskefuld"] = "spsk",
        ["spiseskefulde"] = "spsk",
        ["teskefuld"] = "tsk",
        ["teskefulde"] = "tsk",
        ["stk."] = "stk",
        ["styk"] = "stk",
    };

    /// <summary>The factor from the recipe's own number of people to <paramref name="servings"/>. 1 when the recipe has no number.</summary>
    public static double Factor(Recipe recipe, int servings)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.CanScale && servings > 0 ? (double)servings / recipe.Servings : 1;
    }

    /// <summary>The ingredient list for <paramref name="servings"/> people (the recipe's own number when it has none).</summary>
    public static IReadOnlyList<ScaledIngredient> Scale(Recipe recipe, int servings) =>
        Scale(recipe.Ingredients, Factor(recipe, servings));

    public static IReadOnlyList<ScaledIngredient> Scale(IEnumerable<RecipeIngredient> ingredients, double factor) =>
    [
        .. ingredients.Select(i => i.IsHeading
            ? new ScaledIngredient { Name = i.Name, IsHeading = true }
            : i.Quantity is { } quantity
                ? new ScaledIngredient { Name = i.Name, Unit = i.Unit, Quantity = quantity * factor, AmountText = FormatQuantity(quantity * factor) }
                : new ScaledIngredient { Name = i.Name, Unit = i.Unit, AmountText = i.Amount }),
    ];

    /// <summary>
    /// A quantity the way a Danish recipe writes it: 1.5 → "1½", 0.25 → "¼", 0.3333 → "⅓", 2.4 → "2,4", 1250 → "1250".
    /// Rounded sensibly: whole numbers from 10 up, one decimal below (two below 0,1).
    /// </summary>
    public static string FormatQuantity(double quantity)
    {
        if (double.IsNaN(quantity) || double.IsInfinity(quantity) || quantity <= 0)
        {
            return "0";
        }

        if (quantity >= 10)
        {
            return Math.Round(quantity, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        var whole = Math.Floor(quantity);
        var fraction = quantity - whole;
        foreach (var (value, glyph) in Fractions)
        {
            if (Math.Abs(fraction - value) < 0.02)
            {
                return whole == 0 ? glyph : whole.ToString("0", CultureInfo.InvariantCulture) + glyph;
            }
        }

        if (fraction < 0.02 || fraction > 0.98)
        {
            return Math.Round(quantity).ToString("0", CultureInfo.InvariantCulture);
        }

        var decimals = quantity < 0.1 ? 2 : 1;
        return Math.Round(quantity, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 2 ? "0.##" : "0.#", DanishFormat.Culture);
    }

    private static readonly (double Value, string Glyph)[] Fractions =
    [
        (0.25, "¼"),
        (1.0 / 3, "⅓"),
        (0.5, "½"),
        (2.0 / 3, "⅔"),
        (0.75, "¾"),
    ];

    /// <summary>"spiseskefuld" → "spsk", "g" → "gram" … so the same unit is added up on a shopping list.</summary>
    public static string NormalizeUnit(string? unit)
    {
        var text = (unit ?? "").Trim();
        return UnitSynonyms.TryGetValue(text, out var normalized) ? normalized : text.ToLower(DanishFormat.Culture);
    }

    /// <summary>The key used to recognise the same ingredient in different recipes: "Kærnemælk." ≈ "kærnemælk".</summary>
    public static string NormalizeName(string? name) =>
        string.Join(' ', (name ?? "").Trim().TrimEnd('.', ',', ':').ToLower(DanishFormat.Culture)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// One combined shopping list for several dishes: the same ingredient in the same unit is added up
    /// ("2 dl mælk" + "1½ dl mælk" = "3½ dl mælk"). Sub-headings ("Glasur:") are left out.
    /// Sorted A–Å.
    /// </summary>
    /// <param name="dishes">Each recipe with the number of people it is made for (0 = the recipe's own number).</param>
    public static IReadOnlyList<ShoppingItem> CombineForShopping(IEnumerable<(Recipe Recipe, int Servings)> dishes)
    {
        ArgumentNullException.ThrowIfNull(dishes);
        var groups = new Dictionary<(string Name, string Unit), Accumulator>();
        var order = new List<(string Name, string Unit)>();
        foreach (var (recipe, servings) in dishes)
        {
            var factor = Factor(recipe, servings);
            foreach (var ingredient in recipe.Ingredients.Where(i => !i.IsHeading))
            {
                var key = (NormalizeName(ingredient.Name), NormalizeUnit(ingredient.Unit));
                if (key.Item1.Length == 0)
                {
                    continue;
                }

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Accumulator(ingredient.Name.Trim().TrimEnd('.', ','), key.Item2);
                    groups[key] = group;
                    order.Add(key);
                }

                group.Add(recipe.Id, ingredient, factor);
            }
        }

        var comparer = StringComparer.Create(DanishFormat.Culture, ignoreCase: true);
        return [.. order.Select(k => groups[k].ToItem()).OrderBy(i => i.Name, comparer).ThenBy(i => i.Unit, comparer)];
    }

    private sealed class Accumulator(string name, string unit)
    {
        private readonly List<int> recipeIds = [];
        private readonly List<string> texts = [];
        private double sum;
        private bool allNumbers = true;
        private bool any;

        public void Add(int recipeId, RecipeIngredient ingredient, double factor)
        {
            if (!recipeIds.Contains(recipeId))
            {
                recipeIds.Add(recipeId);
            }

            if (ingredient.Quantity is { } quantity)
            {
                sum += quantity * factor;
                texts.Add(FormatQuantity(quantity * factor));
                any = true;
            }
            else if (ingredient.Amount.Length > 0)
            {
                allNumbers = false;
                texts.Add(ingredient.Amount);
                any = true;
            }
        }

        public ShoppingItem ToItem() => new()
        {
            Name = name,
            Unit = unit,
            Quantity = any && allNumbers ? sum : null,
            AmountText = !any ? "" : allNumbers ? FormatQuantity(sum) : string.Join(" + ", texts),
            RecipeIds = recipeIds,
        };
    }
}
