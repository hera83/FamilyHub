using System.Globalization;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Shopping;

/// <summary>
/// The family's shopping list: works it out for a week from the meal plan and the recipe book
/// (<see cref="ShoppingCalculator"/>) and keeps what the family adds to it – fixed items, their own way of buying a grocery
/// and the week's marks – in the meal plan's database. Shared by every screen (singleton): <see cref="Changed"/> tells the
/// others when something is added, changed or crossed out.
/// </summary>
public sealed class ShoppingListService(
    IDbContextFactory<MealPlanDbContext> databases,
    MealPlanService mealPlan,
    RecipeService recipes,
    IHubClock clock,
    ILogger<ShoppingListService> logger)
{
    /// <summary>Marks and one-week items are kept this many weeks after their week, then cleared away.</summary>
    internal const int KeepWeeks = 8;

    /// <summary>At most this many pack sizes per grocery.</summary>
    public const int MaxPackSizes = 3;

    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Raised after fixed items, rules or marks changed. May come from any thread – use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>
    /// The shopping list for the week containing <paramref name="week"/>, with the dishes from <paramref name="from"/>
    /// (a day in the week; earlier days are left out – e.g. the rest of this week).
    /// </summary>
    public async Task<ShoppingList> GetAsync(DateOnly week, DateOnly from, CancellationToken cancellationToken = default)
    {
        var monday = MealPlanRules.WeekStart(week);
        var first = from < monday ? monday : from;
        var dishes = await mealPlan.GetDishesAsync(first, monday.AddDays(7), cancellationToken);

        await using var db = await databases.CreateDbContextAsync(cancellationToken);
        var items = await db.FixedItems.AsNoTracking().Where(i => i.Week == null || i.Week == monday).ToListAsync(cancellationToken);
        var rules = await db.GroceryRules.AsNoTracking().ToListAsync(cancellationToken);
        var marks = await db.ShoppingMarks.AsNoTracking().Where(m => m.Week == monday).ToListAsync(cancellationToken);

        return ShoppingCalculator.Build(new ShoppingInput
        {
            Week = monday,
            From = first,
            Dishes = [.. dishes.Values.SelectMany(d => d).Select(d => (d, d.RecipeId is { } id ? recipes.Find(id) : null))],
            FixedItems = [.. items.Select(ToModel)],
            Rules = rules.ToDictionary(r => r.Key, ToModel, StringComparer.Ordinal),
            Marks = marks.ToDictionary(m => m.Key, m => m.Marks, StringComparer.Ordinal),
        });
    }

    // ------------------------------------------------------------------ fixed items

    /// <summary>Adds a fixed item. Throws <see cref="ArgumentException"/> without a name.</summary>
    public async Task<FixedItem> AddItemAsync(FixedItemDraft draft, CancellationToken cancellationToken = default)
    {
        var entity = new FixedItemEntity();
        Apply(entity, draft);
        await WriteAsync(async db =>
        {
            db.FixedItems.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return ToModel(entity);
    }

    /// <summary>Changes a fixed item. Null when it no longer exists (removed on another screen).</summary>
    public async Task<FixedItem?> UpdateItemAsync(int id, FixedItemDraft draft, CancellationToken cancellationToken = default)
    {
        FixedItemEntity? entity = null;
        await WriteAsync(async db =>
        {
            entity = await db.FixedItems.FindAsync([id], cancellationToken);
            if (entity is not null)
            {
                Apply(entity, draft);
                await db.SaveChangesAsync(cancellationToken);
            }
        }, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    /// <summary>Removes a fixed item right away and returns it, for "Fortryd" via <see cref="RestoreItemAsync"/>.</summary>
    public async Task<FixedItem?> RemoveItemAsync(int id, CancellationToken cancellationToken = default)
    {
        FixedItem? removed = null;
        await WriteAsync(async db =>
        {
            var entity = await db.FixedItems.FindAsync([id], cancellationToken);
            if (entity is not null)
            {
                removed = ToModel(entity);
                db.FixedItems.Remove(entity);
                await db.SaveChangesAsync(cancellationToken);
            }
        }, cancellationToken);
        return removed;
    }

    /// <summary>"Fortryd": puts a removed fixed item back (with a new id).</summary>
    public Task<FixedItem> RestoreItemAsync(FixedItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return AddItemAsync(new FixedItemDraft(item.Name, item.Quantity, item.Unit, item.Week), cancellationToken);
    }

    /// <summary>Trimmed, single spaces, capital first letter, at most <see cref="FixedItem.MaxNameLength"/> characters. Null when nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        var text = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > FixedItem.MaxNameLength)
        {
            text = text[..FixedItem.MaxNameLength].TrimEnd();
        }

        return text.Length == 0 ? null : DanishFormat.Capitalize(text);
    }

    // ------------------------------------------------------------------ rules

    /// <summary>Saves how the family buys a grocery – it replaces the catalogue's way.</summary>
    public async Task SetRuleAsync(GroceryRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Key) || rule.Key.Length > MealPlanDbContext.MaxKeyLength)
        {
            throw new ArgumentException("Varen mangler.", nameof(rule));
        }

        var sizes = rule.PackSizes.Where(s => s > 0 && double.IsFinite(s)).Distinct().OrderDescending().Take(MaxPackSizes).ToList();
        await WriteAsync(async db =>
        {
            var entity = await db.GroceryRules.FindAsync([rule.Key], cancellationToken);
            if (entity is null)
            {
                entity = new GroceryRuleEntity { Key = rule.Key };
                db.GroceryRules.Add(entity);
            }

            entity.IsStaple = rule.IsStaple;
            entity.Mode = sizes.Count > 0 && rule.PackMeasure is not null ? rule.Mode : BuyMode.Total;
            entity.PackMeasure = sizes.Count > 0 ? rule.PackMeasure : null;
            entity.PackSizes = string.Join(';', sizes.Select(s => s.ToString(CultureInfo.InvariantCulture)));
            entity.Section = rule.Section;
            entity.UpdatedAt = clock.Now;
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>"Brug standard": forgets the family's own rule, so the catalogue's is used again.</summary>
    public Task ResetRuleAsync(string key, CancellationToken cancellationToken = default) =>
        WriteAsync(async db =>
        {
            await db.GroceryRules.Where(r => r.Key == key).ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

    // ------------------------------------------------------------------ marks

    /// <summary>Crosses a grocery out (<see cref="ShoppingMarks.Done"/>) or marks a staple as missing (<see cref="ShoppingMarks.Needed"/>) – or undoes it.</summary>
    public Task SetMarkAsync(DateOnly week, string key, ShoppingMarks mark, bool on, CancellationToken cancellationToken = default)
    {
        var monday = MealPlanRules.WeekStart(week);
        return WriteAsync(async db =>
        {
            var entity = await db.ShoppingMarks.FindAsync([monday, key], cancellationToken);
            var marks = ((entity?.Marks ?? ShoppingMarks.None) & ~mark) | (on ? mark : ShoppingMarks.None);
            if (entity is null && marks != ShoppingMarks.None)
            {
                db.ShoppingMarks.Add(new ShoppingMarkEntity { Week = monday, Key = key, Marks = marks });
            }
            else if (entity is not null && marks == ShoppingMarks.None)
            {
                db.ShoppingMarks.Remove(entity);
            }
            else if (entity is not null)
            {
                entity.Marks = marks;
            }

            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    // ------------------------------------------------------------------ plumbing

    private async Task WriteAsync(Func<MealPlanDbContext, Task> write, CancellationToken cancellationToken)
    {
        await mealPlan.EnsureDatabaseAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await databases.CreateDbContextAsync(cancellationToken);
            await write(db);
            await ForgetOldWeeksAsync(db, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        NotifyChanged();
    }

    // Marks and one-week items for weeks long gone are of no use – keep the tables small.
    private async Task ForgetOldWeeksAsync(MealPlanDbContext db, CancellationToken cancellationToken)
    {
        var before = MealPlanRules.WeekStart(clock.Today).AddDays(-7 * KeepWeeks);
        await db.ShoppingMarks.Where(m => m.Week < before).ExecuteDeleteAsync(cancellationToken);
        await db.FixedItems.Where(i => i.Week != null && i.Week < before).ExecuteDeleteAsync(cancellationToken);
    }

    private void Apply(FixedItemEntity entity, FixedItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        entity.Name = CleanName(draft.Name) ?? throw new ArgumentException("Skriv en vare.", nameof(draft));
        entity.Quantity = Math.Clamp(draft.Quantity, 0, FixedItem.MaxQuantity);
        entity.Unit = entity.Quantity > 0 && FixedItem.Units.Contains(draft.Unit) ? draft.Unit : "";
        entity.Week = draft.Week is { } week ? MealPlanRules.WeekStart(week) : null;
        entity.UpdatedAt = clock.Now;
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A ShoppingListService.Changed handler failed");
        }
    }

    private static FixedItem ToModel(FixedItemEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Quantity = entity.Quantity,
        Unit = entity.Unit,
        Week = entity.Week,
    };

    private static GroceryRule ToModel(GroceryRuleEntity entity) => new()
    {
        Key = entity.Key,
        IsStaple = entity.IsStaple,
        Mode = entity.Mode,
        PackMeasure = entity.PackMeasure,
        PackSizes =
        [
            .. entity.PackSizes.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : 0)
                .Where(s => s > 0),
        ],
        Section = entity.Section,
    };
}
