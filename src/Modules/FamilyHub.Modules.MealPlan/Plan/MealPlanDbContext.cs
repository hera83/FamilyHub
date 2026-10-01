using FamilyHub.Modules.MealPlan.Recipes;
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

/// <summary>
/// The meal plan's database: <c>madplan/madplan.db</c> in the data folder. Only the family's own data –
/// recipes belong to the recipe book and are referenced by id.
/// New migration: <c>dotnet ef migrations add &lt;Name&gt; --project src/Modules/FamilyHub.Modules.MealPlan --output-dir Plan/Migrations</c>.
/// </summary>
public sealed class MealPlanDbContext(DbContextOptions<MealPlanDbContext> options) : DbContext(options)
{
    internal DbSet<DinnerEntity> Dinners => Set<DinnerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dinner = modelBuilder.Entity<DinnerEntity>();
        dinner.ToTable("Dinners");
        dinner.HasKey(d => new { d.Date, d.Course });
        dinner.Property(d => d.Title).HasMaxLength(RecipeDraft.MaxTitleLength).IsRequired();
    }
}

/// <summary>Lets <c>dotnet ef</c> create migrations without starting the app.</summary>
internal sealed class MealPlanDbContextDesignFactory : IDesignTimeDbContextFactory<MealPlanDbContext>
{
    public MealPlanDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite("Data Source=madplan.db").Options);
}
