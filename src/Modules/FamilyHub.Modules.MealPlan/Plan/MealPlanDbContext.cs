using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.Modules.MealPlan.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FamilyHub.Modules.MealPlan.Plan;

/// <summary>One planned dish in the database – at most one per course per date.</summary>
internal sealed class DinnerEntity
{
    public DateOnly Date { get; set; }

    /// <summary>Starter, main course or dessert. Rows from before courses existed are main courses (0).</summary>
    public DinnerCourse Course { get; set; }

    /// <summary>The recipe in the recipe book. Null = the family's own text ("Rester").</summary>
    public int? RecipeId { get; set; }

    /// <summary>The recipe's title when it was chosen (shown if the recipe book can't be reached), or the typed text.</summary>
    public string Title { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A grocery on the shopping list every week ("Mælk", "Skyr") – or in one week only.</summary>
internal sealed class FixedItemEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>0 = no amount.</summary>
    public decimal Quantity { get; set; }

    public string Unit { get; set; } = "";

    /// <summary>Null = every week; otherwise the Monday of its week.</summary>
    public DateOnly? Week { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>How the family buys a grocery (overrides the catalogue). One row per grocery key.</summary>
internal sealed class GroceryRuleEntity
{
    public string Key { get; set; } = "";

    public bool IsStaple { get; set; }

    public BuyMode Mode { get; set; }

    public Measure? PackMeasure { get; set; }

    /// <summary>Base units separated by ";" ("400;500"), largest first.</summary>
    public string PackSizes { get; set; } = "";

    public GrocerySection Section { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A grocery crossed out – or a staple marked as missing – in one week.</summary>
internal sealed class ShoppingMarkEntity
{
    /// <summary>The Monday of the week.</summary>
    public DateOnly Week { get; set; }

    public string Key { get; set; } = "";

    public ShoppingMarks Marks { get; set; }
}

/// <summary>
/// The meal plan's database: <c>madplan/madplan.db</c> in the data folder. Only the family's own data –
/// recipes belong to the recipe book and are referenced by id.
/// New migration: <c>dotnet ef migrations add &lt;Name&gt; --project src/Modules/FamilyHub.Modules.MealPlan --output-dir Plan/Migrations</c>.
/// </summary>
public sealed class MealPlanDbContext(DbContextOptions<MealPlanDbContext> options) : DbContext(options)
{
    /// <summary>Longest grocery key (the cleaned ingredient name).</summary>
    internal const int MaxKeyLength = 200;

    internal DbSet<DinnerEntity> Dinners => Set<DinnerEntity>();

    internal DbSet<FixedItemEntity> FixedItems => Set<FixedItemEntity>();

    internal DbSet<GroceryRuleEntity> GroceryRules => Set<GroceryRuleEntity>();

    internal DbSet<ShoppingMarkEntity> ShoppingMarks => Set<ShoppingMarkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dinner = modelBuilder.Entity<DinnerEntity>();
        dinner.ToTable("Dinners");
        dinner.HasKey(d => new { d.Date, d.Course });
        dinner.Property(d => d.Title).HasMaxLength(RecipeDraft.MaxTitleLength).IsRequired();

        var item = modelBuilder.Entity<FixedItemEntity>();
        item.ToTable("FixedItems");
        item.HasKey(i => i.Id);
        item.Property(i => i.Name).HasMaxLength(FixedItem.MaxNameLength).IsRequired();
        item.Property(i => i.Unit).HasMaxLength(20).IsRequired();
        item.HasIndex(i => i.Week);

        var rule = modelBuilder.Entity<GroceryRuleEntity>();
        rule.ToTable("GroceryRules");
        rule.HasKey(r => r.Key);
        rule.Property(r => r.Key).HasMaxLength(MaxKeyLength);
        rule.Property(r => r.PackSizes).HasMaxLength(100).IsRequired();

        var mark = modelBuilder.Entity<ShoppingMarkEntity>();
        mark.ToTable("ShoppingMarks");
        mark.HasKey(m => new { m.Week, m.Key });
        mark.Property(m => m.Key).HasMaxLength(MaxKeyLength);
    }
}

/// <summary>Lets <c>dotnet ef</c> create migrations without starting the app.</summary>
internal sealed class MealPlanDbContextDesignFactory : IDesignTimeDbContextFactory<MealPlanDbContext>
{
    public MealPlanDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite("Data Source=madplan.db").Options);
}
