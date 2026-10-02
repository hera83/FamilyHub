using System.Globalization;
using System.Text;
using System.Text.Json;
using Bunit;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Printing;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Print;
using FamilyHub.Modules.MealPlan.Recipes;
using FamilyHub.Modules.MealPlan.Shopping;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using QuestPDF.Fluent;

namespace FamilyHub.Tests.MealPlan;

/// <summary>The shopping list: kept in the meal plan's database, shown in its dialog and printed.</summary>
public sealed class ShoppingListTests : BunitContext
{
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly DateOnly Thursday = Monday.AddDays(3);

    private static readonly Recipe Chili = new()
    {
        Id = 2,
        Title = "Chili sin carne",
        Servings = 4,
        Ingredients =
        [
            new() { Name = "squash", Amount = "1", Quantity = 1, Unit = "stk" },
            new() { Name = "hakket oksekød", Amount = "500", Quantity = 500, Unit = "gram" },
            new() { Name = "salt" },
        ],
    };

    private static readonly Recipe Pancakes = new()
    {
        Id = 5,
        Title = "Pandekager",
        Servings = 4,
        Ingredients = [new() { Name = "mælk", Amount = "5", Quantity = 5, Unit = "dl" }, new() { Name = "æg", Amount = "3", Quantity = 3, Unit = "stk" }],
    };

    private static readonly Recipe Lasagne = new()
    {
        Id = 7,
        Title = "Lasagne",
        Servings = 4,
        Ingredients = [new() { Name = "hakket oksekød", Amount = "1250", Quantity = 1250, Unit = "gram" }],
    };

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 30, 8, 0, 0, TimeSpan.Zero)); // Wednesday morning
    private readonly HubClock clock;
    private readonly DbContextOptions<MealPlanDbContext> database;
    private readonly MealPlanService mealPlan;
    private readonly RecipeService recipes;
    private readonly ShoppingListService shopping;

    public ShoppingListTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Directory.CreateDirectory(directory);

        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        database = new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "madplan.db")}").Options;
        mealPlan = new MealPlanService(new Factory(database), clock, NullLogger<MealPlanService>.Instance);

        // A recipe book copy on disk, as the RecipeService would have saved it.
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var snapshot = new RecipeSnapshot { Recipes = [Chili, Pancakes, Lasagne], LastSuccess = DateTimeOffset.UnixEpoch };
        File.WriteAllText(paths.GetFilePath("madplan/opskrifter.json"), JsonSerializer.Serialize(snapshot, HubJson.Files));
        var api = new RecipeApiClient(new NoHttp(), new FixedOptions<RecipeApiOptions>(new RecipeApiOptions { ApiKey = "key" }), NullLogger<RecipeApiClient>.Instance);
        recipes = new RecipeService(api, paths, clock, NullLogger<RecipeService>.Instance);
        shopping = CreateShopping();

        // No printer set up.
        var printOptions = new FixedOptions<PrintApiOptions>(new PrintApiOptions());
        var printing = new PrintService(new PrintApiClient(new NoHttp(), printOptions, NullLogger<PrintApiClient>.Instance), printOptions, TimeProvider.System, NullLogger<PrintService>.Instance);

        Services.AddLogging();
        Services.AddSingleton<IToastService>(new ToastService(TimeProvider.System, NullLogger<ToastService>.Instance));
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton(mealPlan);
        Services.AddSingleton(recipes);
        Services.AddSingleton(shopping);
        Services.AddSingleton(printing);
        Services.AddSingleton(new ShoppingListPrinter(printing, clock, NullLogger<ShoppingListPrinter>.Instance));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        clock.Dispose();
        SqliteConnection.ClearAllPools(); // releases the database file so the folder can be deleted
        Directory.Delete(directory, recursive: true);
    }

    private ShoppingListService CreateShopping() =>
        new(new Factory(database), mealPlan, recipes, clock, NullLogger<ShoppingListService>.Instance);

    private Task<ShoppingList> WeekAsync(ShoppingListService? service = null, DateOnly? from = null) =>
        (service ?? shopping).GetAsync(Monday, from ?? Monday);

    // ------------------------------------------------------------------ the service

    [Fact]
    public async Task Fixed_items_rules_and_marks_are_kept_across_restarts()
    {
        await mealPlan.PlanRecipeAsync(Monday, DinnerCourse.Main, Chili);
        await shopping.AddItemAsync(new FixedItemDraft("  skyr ", 2, "stk", null));
        await shopping.SetRuleAsync(new GroceryRule
        {
            Key = "hakket oksekød", Mode = BuyMode.Packs, PackMeasure = Measure.Mass, PackSizes = [400, 0, 400, 800], Section = GrocerySection.MeatAndFish,
        });
        await shopping.SetMarkAsync(Wednesday, "squash", ShoppingMarks.Done, true);

        var list = await WeekAsync(CreateShopping());

        var skyr = Assert.Single(list.FixedItems);
        Assert.Equal("Skyr", skyr.Name);
        Assert.Equal("2 stk", skyr.AmountText);
        Assert.True(skyr.IsEveryWeek);
        var beef = Assert.Single(list.Lines, l => l.Key == "hakket oksekød");
        Assert.Equal([800, 400], beef.Rule.PackSizes);
        Assert.Equal("800 g", beef.Amount);
        Assert.True(beef.HasOwnRule);
        Assert.True(Assert.Single(list.Lines, l => l.Key == "squash").IsDone);
        Assert.Contains(list.Lines, l => l.Name == "Skyr" && l.Amount == "2 stk");
    }

    [Fact]
    public async Task The_list_follows_the_meal_plan_from_the_chosen_day()
    {
        await mealPlan.PlanRecipeAsync(Monday, DinnerCourse.Main, Chili);
        await mealPlan.PlanRecipeAsync(Thursday, DinnerCourse.Main, Pancakes);
        await mealPlan.PlanTextAsync(Thursday, DinnerCourse.Dessert, "Is");

        var rest = await WeekAsync(from: Wednesday);
        var whole = await WeekAsync();

        Assert.Equal(["Pandekager"], rest.Dishes.Select(d => d.Title));
        Assert.Equal(["Is"], rest.WithoutIngredients.Select(d => d.Title));
        Assert.DoesNotContain(rest.Lines, l => l.Key == "squash");
        Assert.Contains(whole.Lines, l => l.Key == "squash");
        Assert.Equal(["Salt"], whole.Staples.Select(s => s.Name));
        Assert.Equal("1 l", Assert.Single(rest.Lines, l => l.Key == "mælk").Amount);
    }

    [Fact]
    public async Task A_fixed_item_for_one_week_is_only_on_that_weeks_list()
    {
        await shopping.AddItemAsync(new FixedItemDraft("Kage til fødselsdag", 1, "stk", Wednesday));

        Assert.Equal(Monday, Assert.Single((await WeekAsync()).FixedItems).Week);
        Assert.Empty((await shopping.GetAsync(Monday.AddDays(7), Monday.AddDays(7))).FixedItems);
    }

    [Fact]
    public async Task A_fixed_item_can_be_changed_and_removed_with_fortryd()
    {
        var item = await shopping.AddItemAsync(new FixedItemDraft("Mælk", 2, "l", null));
        await shopping.UpdateItemAsync(item.Id, new FixedItemDraft("Letmælk", 3, "pose", null));
        Assert.Equal("3 poser", Assert.Single((await WeekAsync()).FixedItems).AmountText);

        var removed = await shopping.RemoveItemAsync(item.Id);
        Assert.Empty((await WeekAsync()).FixedItems);

        await shopping.RestoreItemAsync(removed!);
        Assert.Equal("Letmælk", Assert.Single((await WeekAsync()).FixedItems).Name);
        Assert.Null(await shopping.RemoveItemAsync(9999));
    }

    [Fact]
    public async Task A_fixed_item_needs_a_name_and_a_known_unit()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => shopping.AddItemAsync(new FixedItemDraft("   ", 1, "stk", null)));

        var odd = await shopping.AddItemAsync(new FixedItemDraft("Rugbrød", 99, "kasser", null));
        var none = await shopping.AddItemAsync(new FixedItemDraft("Frugt til madpakker", 0, "stk", null));

        Assert.Equal(FixedItem.MaxQuantity, odd.Quantity);
        Assert.Equal("", odd.Unit);
        Assert.Equal("", none.Unit);
        Assert.Equal("", none.AmountText);
        Assert.Null(ShoppingListService.CleanName(" \t "));
        Assert.Equal("Pålæg", ShoppingListService.CleanName("  pålæg "));
    }

    [Fact]
    public async Task Brug_standard_forgets_the_familys_rule()
    {
        await mealPlan.PlanRecipeAsync(Monday, DinnerCourse.Main, Pancakes);
        await shopping.SetRuleAsync(new GroceryRule { Key = "mælk", IsStaple = true, Section = GrocerySection.Chilled });
        Assert.Contains((await WeekAsync()).Staples, s => s.Key == "mælk");

        await shopping.ResetRuleAsync("mælk");

        var milk = Assert.Single((await WeekAsync()).Lines, l => l.Key == "mælk");
        Assert.False(milk.HasOwnRule);
        Assert.Equal("1 l", milk.Amount);
    }

    [Fact]
    public async Task Marks_from_weeks_long_gone_are_forgotten()
    {
        var old = Monday.AddDays(-7 * (ShoppingListService.KeepWeeks + 1));
        await shopping.SetMarkAsync(old, "squash", ShoppingMarks.Done, true);
        await shopping.AddItemAsync(new FixedItemDraft("Gammel", 1, "stk", old));

        await shopping.SetMarkAsync(Monday, "squash", ShoppingMarks.Done, true); // any write tidies up

        await using var db = new MealPlanDbContext(database);
        Assert.Equal([Monday], await db.ShoppingMarks.Select(m => m.Week).ToListAsync());
        Assert.Empty(await db.FixedItems.ToListAsync());
    }

    [Fact]
    public async Task Marks_are_set_and_cleared_one_at_a_time()
    {
        await shopping.SetMarkAsync(Monday, "salt", ShoppingMarks.Needed, true);
        await shopping.SetMarkAsync(Monday, "salt", ShoppingMarks.Done, true);
        await shopping.SetMarkAsync(Monday, "salt", ShoppingMarks.Needed, false);

        await using (var db = new MealPlanDbContext(database))
        {
            Assert.Equal(ShoppingMarks.Done, (await db.ShoppingMarks.SingleAsync()).Marks);
        }

        await shopping.SetMarkAsync(Monday, "salt", ShoppingMarks.Done, false);
        await using (var db = new MealPlanDbContext(database))
        {
            Assert.Empty(await db.ShoppingMarks.ToListAsync());
        }
    }

    [Fact]
    public async Task Other_screens_hear_about_changes()
    {
        var changes = 0;
        shopping.Changed += () => changes++;

        var item = await shopping.AddItemAsync(new FixedItemDraft("Skyr", 1, "stk", null));
        await shopping.SetMarkAsync(Monday, "skyr", ShoppingMarks.Done, true);
        await shopping.SetRuleAsync(new GroceryRule { Key = "skyr" });
        await shopping.RemoveItemAsync(item.Id);

        Assert.Equal(4, changes);
    }

    // ------------------------------------------------------------------ the dialog

    private IRenderedComponent<ShoppingListDialog> RenderList(DateOnly today) => Render<ShoppingListDialog>(p => p
        .Add(x => x.Open, true)
        .Add(x => x.Week, Monday)
        .Add(x => x.Today, today));

    [Fact]
    public async Task The_list_shows_the_rest_of_this_week_by_section_with_the_staples_beside_it()
    {
        await mealPlan.PlanRecipeAsync(Monday, DinnerCourse.Main, Pancakes);
        await mealPlan.PlanRecipeAsync(Thursday, DinnerCourse.Main, Chili);

        var cut = RenderList(Wednesday);

        cut.WaitForAssertion(() => Assert.Equal(["Frugt og grønt", "Kød og fisk"], cut.FindAll(".sl-section").Select(e => e.TextContent)));
        Assert.Equal("Indkøbsliste · uge 40", cut.Find(".hub-dialog__title").TextContent);
        Assert.Equal("Resten af ugen", cut.Find(".sl-top [aria-checked='true']").TextContent.Trim());
        Assert.Equal("500 g", cut.Find(".sl-row[data-key='hakket oksekød'] .sl-row__amount").TextContent);
        Assert.Contains("Chili sin carne", cut.Find(".sl-row[data-key='squash'] .sl-row__detail").TextContent);
        Assert.Single(cut.FindAll(".sl-side .sl-row[data-key='salt']"));
        Assert.Empty(cut.FindAll(".sl-main .sl-row[data-key='mælk']")); // Monday's pancakes are eaten

        cut.FindAll(".sl-top .hub-choice__option").Single(o => o.TextContent.Contains("Hele ugen")).Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".sl-main .sl-row[data-key='mælk']")));
        Assert.Equal("1 l", cut.Find(".sl-row[data-key='mælk'] .sl-row__amount").TextContent);
        Assert.Contains("skal bruge 5 dl", cut.Find(".sl-row[data-key='mælk'] .sl-row__detail").TextContent);
    }

    [Fact]
    public async Task Tapping_a_grocery_crosses_it_out_and_tapping_a_staple_puts_it_on_the_list()
    {
        await mealPlan.PlanRecipeAsync(Thursday, DinnerCourse.Main, Chili);
        var cut = RenderList(Wednesday);
        cut.WaitForAssertion(() => cut.Find(".sl-row[data-key='squash']"));

        cut.Find(".sl-row[data-key='squash'] .sl-row__main").Click();
        cut.WaitForAssertion(() => Assert.Contains("sl-row--checked", cut.Find(".sl-row[data-key='squash']").ClassName));
        Assert.Equal("true", cut.Find(".sl-row[data-key='squash'] .sl-row__main").GetAttribute("aria-pressed"));
        await EventuallyAsync(async () => (await WeekAsync()).Lines.Single(l => l.Key == "squash").IsDone);

        cut.Find(".sl-side .sl-row[data-key='salt'] .sl-row__main").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".sl-main .sl-row[data-key='salt']")));
        Assert.Contains("Basisvare", cut.Find(".sl-main .sl-row[data-key='salt']").TextContent);
        Assert.Contains("sl-row--checked", cut.Find(".sl-side .sl-row[data-key='salt']").ClassName);
    }

    [Fact]
    public void An_empty_week_invites_fixed_items_and_the_printer_is_left_out_when_not_set_up()
    {
        var cut = RenderList(Wednesday);

        cut.WaitForAssertion(() => Assert.Equal("Indkøbslisten er tom", cut.Find(".hub-empty__title").TextContent.Trim()));
        Assert.Empty(cut.FindAll(".sl-print"));
        Assert.Empty(cut.FindAll(".sl-side .sl-row"));
    }

    [Fact]
    public async Task Fixed_items_are_added_from_the_list_one_after_the_other()
    {
        var cut = RenderList(Monday);
        cut.WaitForAssertion(() => cut.Find(".sl-group__head .hub-btn"));
        Assert.Empty(cut.FindAll(".sl-top .hub-choice")); // Monday: the whole week, nothing to choose

        cut.Find(".sl-group__head .hub-btn").Click();
        var dialogs = cut.FindAll(".hub-dialog");
        Assert.Equal("Tilføj fast vare", dialogs[^1].QuerySelector(".hub-dialog__title")!.TextContent);

        var input = cut.Find(".hub-dialog input");
        input.Input("skyr");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.Contains("Tilføjet: Skyr", cut.Markup));
        Assert.Equal("", cut.Find(".hub-dialog input").GetAttribute("value") ?? "");

        // Then one for this week only, with its own amount – and "Tilføj" closes.
        cut.Find(".hub-dialog input").Input("Kage");
        cut.FindAll(".hub-dialog .hub-choice__option").Single(o => o.TextContent.Trim() == "Kun uge 40").Click();
        cut.FindAll(".hub-dialog")[^1].QuerySelectorAll(".hub-btn").Single(b => b.TextContent.Trim() == "Tilføj").Click();

        cut.WaitForAssertion(() => Assert.Equal(["Skyr", "Kage"], cut.FindAll(".sl-side .hub-list__title").Select(e => e.TextContent)));
        Assert.Single(cut.FindAll(".hub-dialog"));
        var items = (await WeekAsync()).FixedItems;
        Assert.Equal([null, Monday], items.Select(i => i.Week));
    }

    [Fact]
    public async Task The_pencil_sets_how_a_grocery_is_bought()
    {
        await mealPlan.PlanRecipeAsync(Thursday, DinnerCourse.Main, Lasagne);
        var cut = RenderList(Monday);
        cut.WaitForAssertion(() => Assert.Equal("1,25 kg", cut.Find(".sl-row[data-key='hakket oksekød'] .sl-row__amount").TextContent));

        cut.Find(".sl-row[data-key='hakket oksekød'] .hub-btn").Click();
        Assert.Equal("Hakket oksekød", cut.FindAll(".hub-dialog__title")[^1].TextContent);
        cut.FindAll(".hub-dialog .hub-choice__option").Single(o => o.TextContent.Trim() == "Hele pakker").Click();
        Assert.Contains("500 g", cut.Find(".gr-sizes").TextContent);
        Assert.Contains("3 × 500 g", cut.Find(".gr-preview").TextContent);

        cut.FindAll(".hub-dialog .hub-btn").Single(b => b.TextContent.Trim() == "Tilføj størrelse").Click();
        Assert.Equal(2, cut.FindAll(".gr-size").Count);
        Assert.Contains("2 × 500 g + 250 g", cut.Find(".gr-preview").TextContent); // the preview follows the sizes
        cut.FindAll(".hub-dialog .hub-btn").Single(b => b.TextContent.Trim() == "Gem").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gr-sizes")));
        var rule = Assert.Single((await WeekAsync()).Lines).Rule;
        Assert.Equal([500, 250], rule.PackSizes);
        cut.WaitForAssertion(() => Assert.Equal("2 × 500 g + 250 g", cut.Find(".sl-row[data-key='hakket oksekød'] .sl-row__amount").TextContent));
    }

    // ------------------------------------------------------------------ print

    [Fact]
    public async Task The_printed_list_leaves_out_what_is_crossed_out_and_fits_one_page()
    {
        await mealPlan.PlanRecipeAsync(Thursday, DinnerCourse.Main, Chili);
        await shopping.AddItemAsync(new FixedItemDraft("Skyr", 2, "stk", null));
        await shopping.SetMarkAsync(Monday, "squash", ShoppingMarks.Done, true);
        var list = await WeekAsync();

        Assert.Equal(["Hakket oksekød", "Skyr"], ShoppingListPdf.LinesToPrint(list).Select(l => l.Name));
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(ShoppingListPdf.Create(list, Wednesday), 0, 5));
        Assert.Single(ShoppingListPdf.Compose(list, Wednesday).GenerateImages());

        var big = list with { Lines = [.. Enumerable.Range(1, 120).Select(i => list.Lines[0] with { Key = $"vare {i}", Name = $"Vare {i}", IsDone = false })] };
        Assert.True(ShoppingListPdf.Compose(big, Wednesday).GenerateImages().Count() > 1);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++)
        {
            await Task.Delay(20);
        }

        Assert.True(await condition());
    }

    private sealed class Factory(DbContextOptions<MealPlanDbContext> options) : IDbContextFactory<MealPlanDbContext>
    {
        public MealPlanDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The shopping list must not call the network.");
    }
}
