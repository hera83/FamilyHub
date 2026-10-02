using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>
/// How the family buys a grocery: staple or not, whole packs or the total, and where it is in the shop.
/// The catalogue gives a default (<see cref="ShoppingCalculator.DefaultRule"/>); the family's own rule replaces it.
/// </summary>
public sealed record GroceryRule
{
    /// <summary>The grocery's key (<see cref="GroceryIdentity.Key"/>), e.g. "mælk".</summary>
    public required string Key { get; init; }

    /// <summary>The family always has it – it only goes on the list when they mark it as missing.</summary>
    public bool IsStaple { get; init; }

    public BuyMode Mode { get; init; }

    /// <summary>What <see cref="PackSizes"/> are measured in.</summary>
    public Measure? PackMeasure { get; init; }

    /// <summary>The pack sizes in base units (ml, g, stk), largest first.</summary>
    public IReadOnlyList<double> PackSizes { get; init; } = [];

    public GrocerySection Section { get; init; } = GrocerySection.Other;

    /// <summary>Packs are used: <see cref="BuyMode.Packs"/> with at least one size.</summary>
    public bool UsesPacks => Mode == BuyMode.Packs && PackMeasure is not null && PackSizes.Count > 0;
}

/// <summary>Marks on a line for one week. Stored per week and grocery.</summary>
[Flags]
public enum ShoppingMarks
{
    None = 0,

    /// <summary>Crossed out: the family has it already, or it is bought.</summary>
    Done = 1,

    /// <summary>A staple that is running out – it goes on this week's list.</summary>
    Needed = 2,
}

/// <summary>
/// A grocery the family buys every week ("Mælk", "Skyr", "Pålæg til madpakker") – or only in one week.
/// </summary>
public sealed record FixedItem
{
    public const int MaxNameLength = 60;

    /// <summary>The largest amount the stepper offers.</summary>
    public const int MaxQuantity = 50;

    /// <summary>The units to choose between. Empty = no unit ("2 skyr").</summary>
    public static IReadOnlyList<string> Units { get; } = ["stk", "pakke", "pose", "bakke", "l", "kg"];

    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary>0 = no amount (just the name on the list).</summary>
    public decimal Quantity { get; init; }

    public string Unit { get; init; } = "";

    /// <summary>Null = every week. Otherwise the Monday of the only week it is on the list.</summary>
    public DateOnly? Week { get; init; }

    public bool IsEveryWeek => Week is null;

    /// <summary>"2 stk", "1½ l", "3 pakker" – empty without an amount.</summary>
    public string AmountText => FormatAmount(Quantity, Unit);

    public static string FormatAmount(decimal quantity, string unit)
    {
        if (quantity <= 0)
        {
            return "";
        }

        var number = RecipeMath.FormatQuantity((double)quantity);
        var word = ShoppingUnits.Plural(unit, (double)quantity);
        return word.Length == 0 ? number : $"{number} {word}";
    }

    /// <summary>Half steps for litres and kilos, whole steps for the rest.</summary>
    public static decimal StepFor(string unit) => unit is "l" or "kg" ? 0.5m : 1m;
}

/// <summary>What the family fills in when adding or changing a <see cref="FixedItem"/>.</summary>
public sealed record FixedItemDraft(string Name, decimal Quantity, string Unit, DateOnly? Week);

/// <summary>One amount of something the week needs – an ingredient line or a fixed item – before they are added up.</summary>
/// <param name="Quantity">The amount as a number, or null ("", "lidt").</param>
/// <param name="AmountText">The amount as written when it is not a number ("lidt") – otherwise empty.</param>
/// <param name="Source">Where it comes from: the dish ("Lasagne") or "Fast vare".</param>
/// <param name="IsFixed">A fixed item – it always goes on the list, even if the grocery is a staple.</param>
public sealed record GroceryNeed(string Name, double? Quantity, string Unit, string AmountText, string Source, bool IsFixed = false);

/// <summary>One line on the shopping list: a grocery with everything the week needs of it added up.</summary>
public sealed record ShoppingLine
{
    /// <summary>The grocery's key – what rules and marks are stored by.</summary>
    public required string Key { get; init; }

    /// <summary>"Mælk".</summary>
    public required string Name { get; init; }

    /// <summary>What to buy: "2 l", "2 × 250 g", "800 g + 500 g", "1,3 kg", "3 dåser". Empty when no amount is known.</summary>
    public string Amount { get; init; } = "";

    /// <summary>What the week needs, when that is not what is bought (packs): "1,3 l". Otherwise null.</summary>
    public string? Need { get; init; }

    /// <summary>Where it comes from: the dishes and "Fast vare", in the week's order.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>How it is bought – the family's rule or the catalogue's.</summary>
    public required GroceryRule Rule { get; init; }

    /// <summary>The family has changed how this grocery is bought (<see cref="Rule"/> is theirs).</summary>
    public bool HasOwnRule { get; init; }

    /// <summary>What the dishes need in the main measure (for the rule editor's preview). Null when no amount is known.</summary>
    public Measured? Total { get; init; }

    /// <summary>A staple used by the week's dishes (and not a fixed item).</summary>
    public bool IsStaple { get; init; }

    /// <summary>A staple marked as missing this week – it is on the list.</summary>
    public bool IsNeeded { get; init; }

    /// <summary>Crossed out this week.</summary>
    public bool IsDone { get; init; }

    public GrocerySection Section => Rule.Section;

    /// <summary>On the list to buy (staples only when marked as missing).</summary>
    public bool IsOnList => !IsStaple || IsNeeded;
}

/// <summary>The week's shopping list.</summary>
public sealed record ShoppingList
{
    public static ShoppingList Empty { get; } = new();

    /// <summary>The Monday of the week.</summary>
    public DateOnly Week { get; init; }

    /// <summary>The first day whose dishes are included (Monday, or today when the rest of this week is shown).</summary>
    public DateOnly From { get; init; }

    /// <summary>What to buy, in the shop's order (<see cref="GrocerySections.ShopOrder"/>), then A–Å.</summary>
    public IReadOnlyList<ShoppingLine> Lines { get; init; } = [];

    /// <summary>The staples the dishes use, A–Å – on the list (<see cref="ShoppingLine.IsNeeded"/>) or not.</summary>
    public IReadOnlyList<ShoppingLine> Staples { get; init; } = [];

    /// <summary>The fixed items on this week's list: every week's and this week's own.</summary>
    public IReadOnlyList<FixedItem> FixedItems { get; init; } = [];

    /// <summary>The dishes from the recipe book whose ingredients are on the list, in the week's order.</summary>
    public IReadOnlyList<PlannedDish> Dishes { get; init; } = [];

    /// <summary>Dishes without ingredients: the family's own ("Rester") and recipes no longer in the book.</summary>
    public IReadOnlyList<PlannedDish> WithoutIngredients { get; init; } = [];

    /// <summary>Lines not crossed out.</summary>
    public int Remaining => Lines.Count(l => !l.IsDone);
}

/// <summary>Everything the shopping list is made from – so it can be worked out (and tested) without a database.</summary>
public sealed record ShoppingInput
{
    /// <summary>The Monday of the week.</summary>
    public DateOnly Week { get; init; }

    /// <summary>Dishes before this day are left out (eaten already).</summary>
    public DateOnly From { get; init; }

    /// <summary>The week's dishes, each with its recipe from the book (null for own dishes and deleted recipes).</summary>
    public IReadOnlyList<(PlannedDish Dish, Recipe? Recipe)> Dishes { get; init; } = [];

    public IReadOnlyList<FixedItem> FixedItems { get; init; } = [];

    /// <summary>The family's own rules by grocery key.</summary>
    public IReadOnlyDictionary<string, GroceryRule> Rules { get; init; } = new Dictionary<string, GroceryRule>();

    /// <summary>This week's marks by grocery key.</summary>
    public IReadOnlyDictionary<string, ShoppingMarks> Marks { get; init; } = new Dictionary<string, ShoppingMarks>();
}
