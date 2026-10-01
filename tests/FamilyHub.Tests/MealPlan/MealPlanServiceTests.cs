using FamilyHub.Core.Configuration;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    private static async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<PlannedDish>>> WeekAsync(MealPlanService service) =>
        await service.GetDishesAsync(Monday, Monday.AddDays(7));

    private static async Task<PlannedDish> MainAsync(MealPlanService service, DateOnly day) =>
        (await WeekAsync(service))[day].Single(d => d.Course == DinnerCourse.Main);

    private static async Task<string[]> TitlesAsync(MealPlanService service, DateOnly day) =>
        (await WeekAsync(service)).TryGetValue(day, out var dishes) ? [.. dishes.Select(d => d.Title)] : [];

    [Fact]
    public async Task A_planned_recipe_is_kept_across_restarts()
    {
        await Create().PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(16, "Alm pizzadej"));

        var dinner = await MainAsync(Create(), Monday);

        Assert.Equal(16, dinner.RecipeId);
        Assert.Equal("Alm pizzadej", dinner.Title);
        Assert.True(dinner.IsFromRecipeBook);
    }

    [Fact]
    public async Task One_dish_per_course_so_planning_the_course_again_replaces_it()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(16, "Alm pizzadej"));

        await service.PlanTextAsync(Monday, DinnerCourse.Main, "  pizza   ude ");

        var dinner = (await WeekAsync(service))[Monday].Single();
        Assert.Null(dinner.RecipeId);
        Assert.Equal("Pizza ude", dinner.Title);
    }

    [Fact]
    public async Task A_day_can_have_a_starter_a_main_course_and_a_dessert_in_menu_order()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(2, "Chili sin carne"));
        await service.PlanRecipeAsync(Monday, DinnerCourse.Dessert, Recipe(23, "Appelsin blondie"));
        await service.PlanTextAsync(Monday, DinnerCourse.Starter, "Tomatsuppe");

        var day = (await WeekAsync(Create()))[Monday];

        Assert.Equal([DinnerCourse.Starter, DinnerCourse.Main, DinnerCourse.Dessert], day.Select(d => d.Course));
        Assert.Equal(["Tomatsuppe", "Chili sin carne", "Appelsin blondie"], day.Select(d => d.Title));
    }

    [Fact]
    public async Task Only_the_asked_days_are_returned()
    {
        var service = Create();
        await service.PlanTextAsync(Monday.AddDays(-1), DinnerCourse.Main, "Sidste uge");
        await service.PlanTextAsync(Monday.AddDays(6), DinnerCourse.Dessert, "Søndag");
        await service.PlanTextAsync(Monday.AddDays(7), DinnerCourse.Main, "Næste uge");

        Assert.Equal([Monday.AddDays(6)], (await WeekAsync(service)).Keys);
    }

    [Fact]
    public async Task Moving_to_an_empty_day_moves_the_dish()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(2, "Chili sin carne"));

        await service.MoveAsync(Monday, Monday.AddDays(3));

        var week = await WeekAsync(service);
        Assert.False(week.ContainsKey(Monday));
        Assert.Equal("Chili sin carne", week[Monday.AddDays(3)].Single().Title);
    }

    [Fact]
    public async Task Moving_to_a_planned_day_swaps_the_two_dishes()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(2, "Chili sin carne"));
        await service.PlanTextAsync(Monday.AddDays(1), DinnerCourse.Main, "Rester");

        await service.MoveAsync(Monday, Monday.AddDays(1));

        Assert.Equal("Rester", (await MainAsync(service, Monday)).Title);
        Assert.Equal(2, (await MainAsync(service, Monday.AddDays(1))).RecipeId);
        Assert.Equal(2, (await WeekAsync(service)).Count);
    }

    [Fact]
    public async Task Moving_a_day_onto_a_planned_day_swaps_the_whole_menus()
    {
        // Monday: starter, main course and dessert. Wednesday: only a main course.
        var service = Create();
        var wednesday = Monday.AddDays(2);
        await service.PlanTextAsync(Monday, DinnerCourse.Starter, "Tomatsuppe");
        await service.PlanTextAsync(Monday, DinnerCourse.Main, "Lasagne");
        await service.PlanTextAsync(Monday, DinnerCourse.Dessert, "Is");
        await service.PlanTextAsync(wednesday, DinnerCourse.Main, "Rester");

        await service.MoveAsync(wednesday, Monday);

        Assert.Equal(["Rester"], await TitlesAsync(service, Monday));
        Assert.Equal(["Tomatsuppe", "Lasagne", "Is"], await TitlesAsync(service, wednesday));

        // And back again – the other way round gives the same swap.
        await service.MoveAsync(Monday, wednesday);

        Assert.Equal(["Tomatsuppe", "Lasagne", "Is"], await TitlesAsync(service, Monday));
        Assert.Equal(["Rester"], await TitlesAsync(service, wednesday));
    }

    [Fact]
    public async Task Moving_a_day_to_an_empty_day_takes_every_course_along()
    {
        var service = Create();
        await service.PlanTextAsync(Monday, DinnerCourse.Main, "Lasagne");
        await service.PlanTextAsync(Monday, DinnerCourse.Dessert, "Is");

        await service.MoveAsync(Monday, Monday.AddDays(4));

        Assert.Empty(await TitlesAsync(service, Monday));
        Assert.Equal(["Lasagne", "Is"], await TitlesAsync(service, Monday.AddDays(4)));
    }

    [Fact]
    public async Task Moving_an_empty_day_does_nothing()
    {
        var service = Create();
        await service.PlanTextAsync(Monday.AddDays(1), DinnerCourse.Main, "Rester");
        var changes = 0;
        service.Changed += () => changes++;

        await service.MoveAsync(Monday, Monday.AddDays(1));

        Assert.Equal(["Rester"], await TitlesAsync(service, Monday.AddDays(1)));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_removed_dish_can_be_restored_and_the_rest_of_the_day_stays()
    {
        var service = Create();
        await service.PlanRecipeAsync(Monday, DinnerCourse.Main, Recipe(2, "Chili sin carne"));
        await service.PlanTextAsync(Monday, DinnerCourse.Dessert, "Is");

        var removed = await service.RemoveAsync(Monday, DinnerCourse.Main);
        Assert.Equal(["Is"], await TitlesAsync(service, Monday));

        await service.RestoreAsync(removed!);
        Assert.Equal(2, (await MainAsync(service, Monday)).RecipeId);
        Assert.Null(await service.RemoveAsync(Monday, DinnerCourse.Starter));
        Assert.Null(await service.RemoveAsync(Monday.AddDays(4), DinnerCourse.Main));
    }

    [Fact]
    public async Task Every_change_tells_the_other_screens()
    {
        var service = Create();
        var changes = 0;
        service.Changed += () => changes++;

        await service.PlanTextAsync(Monday, DinnerCourse.Main, "Rester");
        await service.MoveAsync(Monday, Monday.AddDays(1));
        await service.RemoveAsync(Monday.AddDays(1), DinnerCourse.Main);

        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task Dinners_planned_before_courses_existed_become_main_courses()
    {
        // A database as the first version left it: one dinner per date, no course.
        var options = new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "madplan.db")}").Options;
        await using (var db = new MealPlanDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260929091729_Initial");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Dinners (Date, RecipeId, Title, UpdatedAt) VALUES ('2026-09-28', 2, 'Chili sin carne', '2026-09-27T10:00:00+00:00')");
        }

        var service = Create();
        var dinner = (await WeekAsync(service))[Monday].Single();
        Assert.Equal(new PlannedDish(Monday, DinnerCourse.Main, 2, "Chili sin carne"), dinner);

        await service.PlanTextAsync(Monday, DinnerCourse.Dessert, "Is");
        Assert.Equal(["Chili sin carne", "Is"], await TitlesAsync(service, Monday));
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
    public void Moving_onto_a_planned_day_swaps_the_whole_menus()
    {
        DateOnly monday = new(2026, 9, 28), tuesday = new(2026, 9, 29);
        var chili = new PlannedDish(monday, DinnerCourse.Main, 2, "Chili");
        var ice = new PlannedDish(monday, DinnerCourse.Dessert, null, "Is");
        var rest = new PlannedDish(tuesday, DinnerCourse.Main, null, "Rester");

        var result = MealPlanRules.Move([chili, ice], [rest], monday, tuesday);

        Assert.Equal([chili with { Date = tuesday }, ice with { Date = tuesday }, rest with { Date = monday }], result);
        Assert.Equal([chili with { Date = tuesday }], MealPlanRules.Move([chili], [], monday, tuesday));
    }

    private static PlannedDish Dish(DinnerCourse course) => new(new DateOnly(2026, 9, 28), course, null, course.Label());

    [Fact]
    public void The_courses_are_named_and_shown_in_the_order_they_are_eaten()
    {
        Assert.Equal(["Forret", "Hovedret", "Dessert"], DinnerCourses.MenuOrder.Select(c => c.Label()));
        Assert.Equal(DinnerCourse.Main, default(DinnerCourse)); // what the database gives rows from before courses
    }

    [Fact]
    public void Adding_suggests_the_main_course_then_dessert_then_starter()
    {
        Assert.Equal(DinnerCourse.Main, MealPlanRules.NextCourse([]));
        Assert.Equal(DinnerCourse.Dessert, MealPlanRules.NextCourse([Dish(DinnerCourse.Main)]));
        Assert.Equal(DinnerCourse.Starter, MealPlanRules.NextCourse([Dish(DinnerCourse.Main), Dish(DinnerCourse.Dessert)]));
        Assert.Equal(DinnerCourse.Main, MealPlanRules.NextCourse([Dish(DinnerCourse.Dessert)]));
        Assert.Null(MealPlanRules.NextCourse([Dish(DinnerCourse.Starter), Dish(DinnerCourse.Main), Dish(DinnerCourse.Dessert)]));
    }

    [Fact]
    public void Free_courses_are_listed_in_menu_order()
    {
        Assert.Equal([DinnerCourse.Starter, DinnerCourse.Dessert], MealPlanRules.FreeCourses([Dish(DinnerCourse.Main)]));
        Assert.Equal(DinnerCourses.MenuOrder, MealPlanRules.FreeCourses([]));
    }

    [Fact]
    public void The_main_course_stands_for_the_day_and_otherwise_the_first_dish()
    {
        var starter = Dish(DinnerCourse.Starter);
        var main = Dish(DinnerCourse.Main);
        var dessert = Dish(DinnerCourse.Dessert);

        Assert.Equal(main, MealPlanRules.Headline([starter, main, dessert]));
        Assert.Equal(starter, MealPlanRules.Headline([starter, dessert]));
        Assert.Null(MealPlanRules.Headline([]));
    }

    [Fact]
    public void A_course_starts_the_picker_on_its_category_when_the_book_has_one()
    {
        RecipeCategory[] categories =
        [
            new() { Id = 1, Name = "Aftensmad", RecipeCount = 9 },
            new() { Id = 3, Name = "Dessert", RecipeCount = 4 },
            new() { Id = 5, Name = "Forretter", RecipeCount = 0 },
        ];

        Assert.Equal(3, MealPlanRules.CategoryFor(DinnerCourse.Dessert, categories));
        Assert.Null(MealPlanRules.CategoryFor(DinnerCourse.Main, categories));      // the whole book
        Assert.Null(MealPlanRules.CategoryFor(DinnerCourse.Starter, categories));   // an empty category would show nothing
        Assert.Equal(6, MealPlanRules.CategoryFor(DinnerCourse.Starter, [new() { Id = 6, Name = "Forret", RecipeCount = 2 }]));
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
        var dinner = new PlannedDish(new DateOnly(2026, 9, 28), DinnerCourse.Main, 2, "Chili");

        Assert.Equal("Chili sin carne", DinnerLook.Title(dinner, new Recipe { Id = 2, Title = "Chili sin carne" }));
        Assert.Equal("Chili", DinnerLook.Title(dinner, null)); // deleted in the book, or not fetched yet
    }
}
