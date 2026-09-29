using FamilyHub.Core.Configuration;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.MealPlan;

public sealed class MealPlanServiceTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 9, 28);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));
    private readonly List<IDisposable> disposables = [];

    public MealPlanServiceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        SqliteConnection.ClearAllPools(); // releases the database file so the folder can be deleted
        Directory.Delete(directory, recursive: true);
    }

    private MealPlanService Create()
    {
        var clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        disposables.Add(clock);
        var options = new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "madplan.db")}").Options;
        return new MealPlanService(new Factory(options), clock, NullLogger<MealPlanService>.Instance);
    }

    private static Recipe Recipe(int id, string title) => new() { Id = id, Title = title };

    private static async Task<IReadOnlyDictionary<DateOnly, PlannedDinner>> WeekAsync(MealPlanService service) =>
        await service.GetDinnersAsync(Monday, Monday.AddDays(7));

    [Fact]
    public async Task A_planned_recipe_is_kept_across_restarts()
    {
        await Create().PlanRecipeAsync(Monday, Recipe(16, "Alm pizzadej"));

        var week = await WeekAsync(Create());

        var dinner = week[Monday];
        Assert.Equal(16, dinner.RecipeId);
        Assert.Equal("Alm pizzadej", dinner.Title);
        Assert.True(dinner.IsFromRecipeBook);
    }

    [Fact]
    public async Task One_dinner_per_day_so_planning_again_replaces_it()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, Recipe(16, "Alm pizzadej"));

        await service.PlanTextAsync(Monday, "  pizza   ude ");

        var dinner = (await WeekAsync(service)).Values.Single();
        Assert.Null(dinner.RecipeId);
        Assert.Equal("Pizza ude", dinner.Title);
    }

    [Fact]
    public async Task Only_the_asked_days_are_returned()
    {
        var service = Create();
        await service.PlanTextAsync(Monday.AddDays(-1), "Sidste uge");
        await service.PlanTextAsync(Monday.AddDays(6), "Søndag");
        await service.PlanTextAsync(Monday.AddDays(7), "Næste uge");

        Assert.Equal([Monday.AddDays(6)], (await WeekAsync(service)).Keys);
    }

    [Fact]
    public async Task Moving_to_an_empty_day_moves_the_dinner()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, Recipe(2, "Chili sin carne"));

        await service.MoveAsync(Monday, Monday.AddDays(3));

        var week = await WeekAsync(service);
        Assert.False(week.ContainsKey(Monday));
        Assert.Equal("Chili sin carne", week[Monday.AddDays(3)].Title);
    }

    [Fact]
    public async Task Moving_to_a_planned_day_swaps_the_two_dinners()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, Recipe(2, "Chili sin carne"));
        await service.PlanTextAsync(Monday.AddDays(1), "Rester");

        await service.MoveAsync(Monday, Monday.AddDays(1));

        var week = await WeekAsync(service);
        Assert.Equal("Rester", week[Monday].Title);
        Assert.Equal(2, week[Monday.AddDays(1)].RecipeId);
        Assert.Equal(2, week.Count);
    }

    [Fact]
    public async Task Moving_an_empty_day_does_nothing()
    {
        var service = Create();
        await service.PlanTextAsync(Monday.AddDays(1), "Rester");

        await service.MoveAsync(Monday, Monday.AddDays(1));

        Assert.Equal("Rester", (await WeekAsync(service))[Monday.AddDays(1)].Title);
    }

    [Fact]
    public async Task A_removed_dinner_can_be_restored()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, Recipe(2, "Chili sin carne"));

        var removed = await service.RemoveAsync(Monday);
        Assert.Empty(await WeekAsync(service));

        await service.RestoreAsync(removed!);
        Assert.Equal(2, (await WeekAsync(service))[Monday].RecipeId);
        Assert.Null(await service.RemoveAsync(Monday.AddDays(4)));
    }

    [Fact]
    public async Task Every_change_tells_the_other_screens()
    {
        var service = Create();
        var changes = 0;
        service.Changed += () => changes++;

        await service.PlanTextAsync(Monday, "Rester");
        await service.MoveAsync(Monday, Monday.AddDays(1));
        await service.RemoveAsync(Monday.AddDays(1));

        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData("  rester ", "Rester")]
    [InlineData("pizza\n  ude", "Pizza ude")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Own_dishes_are_cleaned_up(string? typed, string? expected)
    {
        Assert.Equal(expected, MealPlanService.CleanTitle(typed));
    }

    [Fact]
    public void Own_dishes_are_limited_in_length()
    {
        Assert.Equal(MealPlanService.MaxTitleLength, MealPlanService.CleanTitle(new string('a', 300))!.Length);
    }

    private sealed class Factory(DbContextOptions<MealPlanDbContext> options) : IDbContextFactory<MealPlanDbContext>
    {
        public MealPlanDbContext CreateDbContext() => new(options);
    }
}

public class MealPlanRulesTests
{
    [Theory]
    [InlineData("2026-09-28", "2026-09-28")]
    [InlineData("2026-10-01", "2026-09-28")]
    [InlineData("2026-10-04", "2026-09-28")]
    public void Weeks_start_on_monday(string date, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday), MealPlanRules.WeekStart(DateOnly.Parse(date)));
        Assert.Equal(7, MealPlanRules.WeekDays(DateOnly.Parse(date)).Count);
    }

    [Theory]
    [InlineData("2026-09-30", "Denne uge · 28. september – 4. oktober")]
    [InlineData("2026-10-05", "Næste uge · 5.–11. oktober")]
    [InlineData("2026-09-21", "Sidste uge · 21.–27. september")]
    [InlineData("2026-10-19", "19.–25. oktober")]
    [InlineData("2026-12-28", "28. december 2026 – 3. januar 2027")]
    [InlineData("2030-01-07", "7.–13. januar 2030")]
    public void The_week_is_described_relative_to_today(string date, string expected)
    {
        Assert.Equal(expected, MealPlanRules.WeekDetail(DateOnly.Parse(date), new DateOnly(2026, 9, 29)));
    }

    [Fact]
    public void Week_title_uses_the_danish_week_number()
    {
        Assert.Equal("Uge 40", MealPlanRules.WeekTitle(new DateOnly(2026, 9, 29)));
        Assert.Equal("Uge 53", MealPlanRules.WeekTitle(new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Moving_onto_a_planned_day_is_a_swap()
    {
        var chili = new PlannedDinner(new DateOnly(2026, 9, 28), 2, "Chili");
        var rest = new PlannedDinner(new DateOnly(2026, 9, 29), null, "Rester");

        var result = MealPlanRules.Move(chili, rest, chili.Date, rest.Date);

        Assert.Equal([(chili, rest.Date), (rest, chili.Date)], result);
        Assert.Equal([(chili, rest.Date)], MealPlanRules.Move(chili, null, chili.Date, rest.Date));
    }
}

public class DinnerLookTests
{
    [Fact]
    public void The_summary_leaves_out_what_the_recipe_book_does_not_know()
    {
        Assert.Equal("45 min · Aftensmad", DinnerLook.Summary(new Recipe { Id = 1, Title = "Chili", TotalTimeMinutes = 45, Category = "Aftensmad" }));
        Assert.Equal("Kager", DinnerLook.Summary(new Recipe { Id = 1, Title = "Kage", Category = "Kager" }));
        Assert.Equal("", DinnerLook.Summary(new Recipe { Id = 1, Title = "Uden noget" }));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1 person")]
    [InlineData(4, "4 personer")]
    public void Servings_are_written_out(int servings, string? expected)
    {
        Assert.Equal(expected, DinnerLook.Servings(new Recipe { Id = 1, Title = "x", Servings = servings }));
    }

    [Fact]
    public void A_renamed_recipe_shows_its_new_title()
    {
        var dinner = new PlannedDinner(new DateOnly(2026, 9, 28), 2, "Chili");

        Assert.Equal("Chili sin carne", DinnerLook.Title(dinner, new Recipe { Id = 2, Title = "Chili sin carne" }));
        Assert.Equal("Chili", DinnerLook.Title(dinner, null)); // deleted in the book, or not fetched yet
    }
}
