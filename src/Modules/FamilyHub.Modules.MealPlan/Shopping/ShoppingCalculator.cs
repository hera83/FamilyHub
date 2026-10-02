using System.Globalization;
using System.Text.RegularExpressions;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;

namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>
/// The helper behind the shopping list: adds up the week's ingredients and fixed items per grocery and says what to buy.
/// <list type="bullet">
/// <item>Units are converted, so "2 dl mælk" + "300 ml mælk" = 5 dl – and weight and volume meet where the grocery's
/// weight per dl is known ("1000 g kærnemælk" = 1 l).</item>
/// <item>Groceries sold in packs are rounded up to whole packs: 1,3 l milk → "2 l", 320 g butter → "2 × 250 g",
/// 1,3 kg minced beef with the family's pack sizes → "800 g + 500 g" (<see cref="PackPlanner"/>).</item>
/// <item>Everything else shows the total ("1,3 kg hakket oksekød"); pieces are rounded up ("½ løg" → 1 stk).</item>
/// <item>Staples (salt, pepper, oil) are kept off the list until the family marks them as missing.</item>
/// </list>
/// Pure functions – unit-tested.
/// </summary>
public static partial class ShoppingCalculator
{
    /// <summary>The source shown for a fixed item that is on the list every week.</summary>
    public const string EveryWeekSource = "Fast vare";

    /// <summary>The source shown for an item added for one week only.</summary>
    public const string ThisWeekSource = "Kun denne uge";

    // Units a fixed item of a grocery sold in one size can be counted in: "2 stk mælk" = 2 × 1 l.
    private static readonly HashSet<string> PackWords = new(StringComparer.OrdinalIgnoreCase) { "pakke", "karton" };

    [GeneratedRegex(@"^\s*(\d+(?:[.,]\d+)?)\s*[-–]\s*(\d+(?:[.,]\d+)?)\s*$")]
    private static partial Regex Range();

    /// <summary>The whole shopping list for a week.</summary>
    public static ShoppingList Build(ShoppingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fixedItems = input.FixedItems
            .Where(i => i.Week is null || i.Week == input.Week)
            .OrderBy(i => i.Week is not null)
            .ThenBy(i => i.Name, StringComparer.Create(DanishFormat.Culture, ignoreCase: true))
            .ToList();
        var needs = fixedItems.Select(NeedFrom).ToList();

        var withRecipe = new List<PlannedDish>();
        var without = new List<PlannedDish>();
        foreach (var (dish, recipe) in input.Dishes
                     .Where(d => d.Dish.Date >= input.From && d.Dish.Date < input.Week.AddDays(7))
                     .OrderBy(d => d.Dish.Date)
                     .ThenBy(d => d.Dish.Course.Position()))
        {
            if (recipe is null)
            {
                without.Add(dish);
                continue;
            }

            withRecipe.Add(dish);
            needs.AddRange(NeedsFrom(recipe, recipe.Title));
        }

        var lines = Combine(needs, input.Rules, input.Marks);
        var comparer = StringComparer.Create(DanishFormat.Culture, ignoreCase: true);
        return new ShoppingList
        {
            Week = input.Week,
            From = input.From,
            Lines = [.. lines.Where(l => l.IsOnList)],
            Staples = [.. lines.Where(l => l.IsStaple).OrderBy(l => l.Name, comparer)],
            FixedItems = fixedItems,
            Dishes = withRecipe,
            WithoutIngredients = without,
        };
    }

    /// <summary>
    /// A recipe's ingredient lines as needs – without sub-headings. A range ("700-800 g mel") counts as its largest number.
    /// </summary>
    public static IEnumerable<GroceryNeed> NeedsFrom(Recipe recipe, string source)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient.IsHeading || string.IsNullOrWhiteSpace(ingredient.Name))
            {
                continue;
            }

            var quantity = ingredient.Quantity ?? UpperOfRange(ingredient.Amount);
            yield return new GroceryNeed(ingredient.Name, quantity, ingredient.Unit, quantity is null ? ingredient.Amount.Trim() : "", source);
        }
    }

    public static GroceryNeed NeedFrom(FixedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new GroceryNeed(item.Name, item.Quantity > 0 ? (double)item.Quantity : null, item.Unit, "",
            item.IsEveryWeek ? EveryWeekSource : ThisWeekSource, IsFixed: true);
    }

    /// <summary>"700-800" → 800, "2–3" → 3. Null when the text is not a range.</summary>
    public static double? UpperOfRange(string? amount)
    {
        var match = Range().Match(amount ?? "");
        return match.Success && double.TryParse(match.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var upper)
            ? upper
            : null;
    }

    /// <summary>How a grocery is bought when the family has not said otherwise – from the catalogue.</summary>
    public static GroceryRule DefaultRule(GroceryIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        // Pack sizes belong to the grocery itself – "røget laks" borrows Laks' section, not how Laks is sold.
        var packs = identity.IsExact ? identity.Grocery : null;
        return new GroceryRule
        {
            Key = identity.Key,
            IsStaple = identity.Grocery?.IsStaple ?? false,
            Mode = packs is { PackSizes.Count: > 0 } ? BuyMode.Packs : BuyMode.Total,
            PackMeasure = packs?.PackMeasure,
            PackSizes = [.. (packs?.PackSizes ?? []).OrderDescending()],
            Section = identity.Grocery?.Section ?? GrocerySection.Other,
        };
    }

    /// <summary>
    /// What to buy for <paramref name="total"/> by <paramref name="rule"/>: whole packs ("2 × 250 g"), or the total
    /// ("1,3 kg"; pieces rounded up). Also what the rule editor previews.
    /// </summary>
    public static (string Buy, string? Need) Buy(Measured total, GroceryRule rule, string? preferredUnit = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var exact = ShoppingUnits.Format(total, preferredUnit);
        if (rule.UsesPacks && rule.PackMeasure == total.Measure && total.Measure != Measure.Other)
        {
            var packs = PackPlanner.Plan(total.Value, rule.PackSizes);
            if (packs.Count > 0)
            {
                var buy = PackPlanner.Format(packs, total.Measure);
                return (buy, buy == exact ? null : exact);
            }
        }

        if (total.Measure == Measure.Count)
        {
            var whole = Math.Ceiling(total.Value - 1e-9);
            var buy = ShoppingUnits.Format(total with { Value = whole });
            return (buy, buy == exact ? null : exact);
        }

        return (exact, null);
    }

    /// <summary>
    /// Adds up the needs per grocery (<see cref="GroceryCatalog.Identify"/>) into lines in the shop's order.
    /// Tap water is left out. Staples are included, flagged <see cref="ShoppingLine.IsStaple"/>.
    /// </summary>
    public static IReadOnlyList<ShoppingLine> Combine(
        IEnumerable<GroceryNeed> needs,
        IReadOnlyDictionary<string, GroceryRule> rules,
        IReadOnlyDictionary<string, ShoppingMarks> marks)
    {
        var groups = new Dictionary<string, (GroceryIdentity Identity, List<GroceryNeed> Needs)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var need in needs)
        {
            var identity = GroceryCatalog.Identify(need.Name);
            if (identity.Key.Length == 0 || (identity.IsExact && identity.Grocery is { IsFree: true }))
            {
                continue;
            }

            if (!groups.TryGetValue(identity.Key, out var group))
            {
                group = (identity, []);
                groups[identity.Key] = group;
                order.Add(identity.Key);
            }

            group.Needs.Add(need);
        }

        var comparer = StringComparer.Create(DanishFormat.Culture, ignoreCase: true);
        return
        [
            .. order
                .Select(key => ToLine(groups[key].Identity, groups[key].Needs, rules.GetValueOrDefault(key), marks.GetValueOrDefault(key)))
                .OrderBy(l => l.Section.Position())
                .ThenBy(l => l.Name, comparer),
        ];
    }

    private static ShoppingLine ToLine(GroceryIdentity identity, List<GroceryNeed> needs, GroceryRule? ownRule, ShoppingMarks mark)
    {
        var rule = ownRule ?? DefaultRule(identity);
        var grocery = identity.IsExact ? identity.Grocery : null;

        // Every amount in its base unit, remembering the largest unit the recipes used per measure ("dl" rather than "spsk").
        var amounts = new List<Measured>();
        var preferred = new Dictionary<Measure, (string Unit, double Factor)>();
        var texts = new List<string>();
        var ownUnits = new Dictionary<string, double>(StringComparer.Ordinal); // "fed" → 6, to say "skal bruge 6 fed"
        foreach (var need in needs)
        {
            if (need.Quantity is { } quantity && quantity > 0)
            {
                var amount = ToBase(quantity, need.Unit, grocery);
                amounts.Add(amount);
                if (IsOwnUnit(need.Unit, grocery, out var own))
                {
                    ownUnits[own] = ownUnits.GetValueOrDefault(own) + quantity;
                }

                if (ShoppingUnits.Factor(need.Unit, amount.Measure) is { } factor
                    && (!preferred.TryGetValue(amount.Measure, out var best) || factor > best.Factor))
                {
                    preferred[amount.Measure] = (RecipeMath.NormalizeUnit(need.Unit), factor);
                }
            }
            else if (need.AmountText.Length > 0 && !texts.Contains(need.AmountText, StringComparer.OrdinalIgnoreCase))
            {
                texts.Add(need.AmountText);
            }
        }

        // The main measure: the one the packs are in, else the one most amounts use.
        var main = rule.UsesPacks && rule.PackMeasure is { } packMeasure
            ? (Measure: packMeasure, Unit: "")
            : amounts.GroupBy(a => (a.Measure, a.Unit)).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        double total = 0;
        var any = false;
        var rest = new List<Measured>();
        foreach (var amount in amounts)
        {
            if (TryConvert(amount, main.Measure, main.Unit, grocery, rule) is { } converted)
            {
                total += converted;
                any = true;
            }
            else
            {
                var same = rest.FindIndex(r => r.Measure == amount.Measure && r.Unit == amount.Unit);
                if (same >= 0)
                {
                    rest[same] = rest[same] with { Value = rest[same].Value + amount.Value };
                }
                else
                {
                    rest.Add(amount);
                }
            }
        }

        Measured? mainTotal = any ? new Measured(total, main.Measure, main.Unit) : null;
        var parts = new List<string>();
        string? needText = null;
        if (mainTotal is { } t)
        {
            (var buy, needText) = Buy(t, rule, preferred.GetValueOrDefault(t.Measure).Unit);
            parts.Add(buy);

            // Only garlic cloves: the need is said in cloves, not "0,6 stk".
            if (needText is not null && ownUnits.Count == 1 && needs.Where(n => n.Quantity > 0).All(n => IsOwnUnit(n.Unit, grocery, out _)))
            {
                var (unit, sum) = ownUnits.Single();
                needText = $"{RecipeMath.FormatQuantity(sum)} {ShoppingUnits.Plural(unit, sum)}";
            }
        }

        parts.AddRange(rest.Select(r => ShoppingUnits.Format(r, preferred.GetValueOrDefault(r.Measure).Unit)));
        parts.AddRange(texts);

        var fixedNeed = needs.FirstOrDefault(n => n.IsFixed);
        var isStaple = rule.IsStaple && fixedNeed is null;
        return new ShoppingLine
        {
            Key = identity.Key,
            Name = fixedNeed is not null ? DanishFormat.Capitalize(fixedNeed.Name.Trim()) : identity.Name,
            Amount = string.Join(" + ", parts),
            Need = needText,
            Sources = [.. needs.Select(n => n.Source).Distinct()],
            Rule = rule,
            HasOwnRule = ownRule is not null,
            Total = mainTotal,
            IsStaple = isStaple,
            IsNeeded = isStaple && mark.HasFlag(ShoppingMarks.Needed),
            IsDone = mark.HasFlag(ShoppingMarks.Done),
        };
    }

    /// <summary>The amount in its base unit, using the grocery's own units first ("2 fed hvidløg" = 0,2 stk).</summary>
    private static Measured ToBase(double quantity, string unit, Grocery? grocery) =>
        IsOwnUnit(unit, grocery, out var own) ? grocery!.Units[own] with { Value = grocery.Units[own].Value * quantity } : ShoppingUnits.ToBase(quantity, unit);

    private static bool IsOwnUnit(string unit, Grocery? grocery, out string own)
    {
        own = ShoppingUnits.Singular(RecipeMath.NormalizeUnit(unit));
        return grocery is not null && grocery.Units.ContainsKey(own);
    }

    /// <summary>The amount in the main measure, or null when it cannot be converted (it is then shown on its own: "+ 2 dåser").</summary>
    private static double? TryConvert(Measured amount, Measure measure, string unit, Grocery? grocery, GroceryRule rule)
    {
        if (amount.Measure == measure && amount.Unit == unit)
        {
            return amount.Value;
        }

        // Weight ↔ volume when the grocery's weight per dl is known.
        if (grocery?.GramsPerDl is { } gramsPerDl and > 0)
        {
            if (amount.Measure == Measure.Volume && measure == Measure.Mass)
            {
                return amount.Value / 100 * gramsPerDl;
            }

            if (amount.Measure == Measure.Mass && measure == Measure.Volume)
            {
                return amount.Value / gramsPerDl * 100;
            }
        }

        // "2 stk mælk" or "1 pakke smør" = whole packs, when the grocery comes in one size.
        if (rule.UsesPacks && rule.PackMeasure == measure && rule.PackSizes.Count == 1
            && (amount.Measure == Measure.Count || (amount.Measure == Measure.Other && PackWords.Contains(amount.Unit))))
        {
            return amount.Value * rule.PackSizes[0];
        }

        return null;
    }
}
