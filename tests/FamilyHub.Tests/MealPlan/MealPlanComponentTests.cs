using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Mambeno;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Printing;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Print;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.MealPlan;

public sealed class MealPlanComponentTests : BunitContext
{
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly IReadOnlyList<DateOnly> Week = MealPlanRules.WeekDays(Monday);

    private static readonly Recipe Chili = new()
    {
        Id = 2,
        Title = "Chili sin carne",
        CategoryId = 1,
        Category = "Aftensmad",
        TotalTimeMinutes = 35,
        Servings = 4,
        Difficulty = RecipeDifficulty.Easy,
        Ingredients = [new() { Name = "Bønner", Amount = "2", Quantity = 2, Unit = "dåse" }, new() { Name = "Topping:" }, new() { Name = "Creme fraiche" }],
    };

    private static readonly Recipe Blondie = new() { Id = 23, Title = "Appelsin blondie", CategoryId = 4, Category = "Kager", Servings = 24 };

    private static readonly Recipe IceCream = new() { Id = 31, Title = "Is med bær", CategoryId = 5, Category = "Dessert" };

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));

    public MealPlanComponentTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IToastService>(new ToastService(TimeProvider.System, NullLogger<ToastService>.Instance));

        // A recipe book copy on disk, as the RecipeService would have saved it.
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var snapshot = new RecipeSnapshot
        {
            Recipes = [Blondie, Chili, IceCream],
            Lookups = new RecipeLookups
            {
                Categories =
                [
                    new() { Id = 1, Name = "Aftensmad", RecipeCount = 1 },
                    new() { Id = 5, Name = "Dessert", RecipeCount = 1 },
                    new() { Id = 4, Name = "Kager", RecipeCount = 1 },
                ],
            },
            LastSuccess = DateTimeOffset.UnixEpoch,
        };
        File.WriteAllText(paths.GetFilePath("madplan/opskrifter.json"), JsonSerializer.Serialize(snapshot, HubJson.Files));

        var api = new RecipeApiClient(new PrintHttp(book), new FixedOptions(new RecipeApiOptions { ApiKey = "key" }), NullLogger<RecipeApiClient>.Instance);
        var clock = new HubClock(TimeProvider.System, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton(new RecipeService(api, paths, clock, NullLogger<RecipeService>.Instance));

        // Mambeno ("Inspiration"): answers at once in tests.
        Services.AddSingleton<IMambenoService>(mambeno);
        InspirationResults.SearchDelay = TimeSpan.Zero;

        // The printer: a print server that accepts everything (printer 7 is chosen in the settings).
        var printApi = new PrintApiClient(new PrintHttp(printServer), new FixedPrintOptions(printOptions), NullLogger<PrintApiClient>.Instance);
        var printing = new PrintService(printApi, new FixedPrintOptions(printOptions), TimeProvider.System, NullLogger<PrintService>.Instance);
        Services.AddSingleton(printing);
        Services.AddSingleton(new RecipePrinter(printing, new NoHttp(), clock, NullLogger<RecipePrinter>.Instance));
    }

    private readonly PrintApiOptions printOptions = new() { BaseUrl = "http://print.test:8080", ApiKey = "ak_test", PrinterId = 7 };
    private readonly PrintServerStub printServer = new();
    private readonly RecipeBookStub book = new();
    private readonly FakeMambeno mambeno = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_week_shows_seven_days_with_dinners_and_empty_days_to_fill()
    {
        DateOnly? added = null;
        PlannedDish? tapped = null;
        var dishes = new Dictionary<DateOnly, IReadOnlyList<PlannedDish>>
        {
            [Tuesday] = [new(Tuesday, DinnerCourse.Main, 2, "Chili")],
            [Monday.AddDays(3)] = [new(Monday.AddDays(3), DinnerCourse.Main, null, "Rester")],
        };

        var cut = Render<MealPlanWeek>(p => p
            .Add(x => x.Days, Week)
            .Add(x => x.Dishes, dishes)
            .Add(x => x.FindRecipe, id => id == 2 ? Chili : null)
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnAdd, (DateOnly d) => added = d)
            .Add(x => x.OnDishSelected, (PlannedDish d) => tapped = d));

        Assert.Equal(7, cut.FindAll(".mp-day").Count);
        Assert.Contains("mp-day--today", cut.Find($".mp-day[data-day='2026-09-29']").ClassName);
        Assert.Equal("Chili sin carne", cut.Find(".mp-day--today .mp-dinner__title").TextContent); // the recipe's current title
        Assert.Equal("35 min · Aftensmad", cut.Find(".mp-day--today .mp-dinner__meta").TextContent);
        Assert.Equal("Egen ret", cut.Find(".mp-day[data-day='2026-10-01'] .mp-dinner__meta").TextContent);
        Assert.Empty(cut.FindAll(".mp-day[data-day='2026-09-28'] .mp-day__empty")); // past days don't invite planning
        Assert.Equal(4, cut.FindAll(".mp-day__empty").Count);

        cut.Find(".mp-day[data-day='2026-10-02'] .mp-day__empty").Click();
        Assert.Equal(Monday.AddDays(4), added);

        cut.Find(".mp-day--today .mp-dish").Click();
        Assert.Equal(DinnerCourse.Main, tapped?.Course);
        Assert.Equal(Tuesday, tapped?.Date);
    }

    [Fact]
    public void A_planned_day_shows_its_courses_in_menu_order_with_a_small_plus_for_one_more()
    {
        DateOnly? added = null;
        var dishes = new Dictionary<DateOnly, IReadOnlyList<PlannedDish>>
        {
            [Monday] = [new(Monday, DinnerCourse.Main, null, "Sidste uges ret")],
            [Tuesday] = [new(Tuesday, DinnerCourse.Main, 2, "Chili"), new(Tuesday, DinnerCourse.Dessert, 23, "Blondie")],
            [Monday.AddDays(2)] =
            [
                new(Monday.AddDays(2), DinnerCourse.Starter, null, "Tomatsuppe"),
                new(Monday.AddDays(2), DinnerCourse.Main, null, "Lasagne"),
                new(Monday.AddDays(2), DinnerCourse.Dessert, null, "Is"),
            ],
        };

        var cut = Render<MealPlanWeek>(p => p
            .Add(x => x.Days, Week)
            .Add(x => x.Dishes, dishes)
            .Add(x => x.FindRecipe, id => id == 2 ? Chili : id == 23 ? Blondie : null)
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnAdd, (DateOnly d) => added = d));

        var tuesday = cut.Find(".mp-day[data-day='2026-09-29']");
        Assert.Equal(["Chili sin carne", "Appelsin blondie"], tuesday.QuerySelectorAll(".mp-dinner__title").Select(e => e.TextContent));
        Assert.Equal(["Dessert"], tuesday.QuerySelectorAll(".mp-dinner__course").Select(e => e.TextContent));
        Assert.Equal(["Main", "Dessert"], tuesday.QuerySelectorAll("[data-course]").Select(e => e.GetAttribute("data-course")));
        Assert.Equal("2026-09-29", tuesday.QuerySelector(".mp-menu")!.GetAttribute("data-dinner")); // a drag lifts the whole menu
        Assert.Equal(2, tuesday.QuerySelectorAll(".mp-menu .mp-dinner").Length);

        var wednesday = cut.Find(".mp-day[data-day='2026-09-30']");
        Assert.Equal(["Tomatsuppe", "Lasagne", "Is"], wednesday.QuerySelectorAll(".mp-dinner__title").Select(e => e.TextContent));
        Assert.Equal(["Forret", "Dessert"], wednesday.QuerySelectorAll(".mp-dinner__course").Select(e => e.TextContent));

        // "Tilføj" under the dishes – not on a full day, and not in the past.
        Assert.Single(tuesday.QuerySelectorAll(".mp-day__add"));
        Assert.Empty(wednesday.QuerySelectorAll(".mp-day__add"));
        Assert.Empty(cut.FindAll(".mp-day[data-day='2026-09-28'] .mp-day__add"));

        cut.Find(".mp-day[data-day='2026-09-29'] .mp-day__add").Click();
        Assert.Equal(Tuesday, added);
    }

    [Fact]
    public async Task A_drop_from_the_browser_becomes_a_move()
    {
        DinnerMove? moved = null;
        var cut = Render<MealPlanWeek>(p => p
            .Add(x => x.Days, Week)
            .Add(x => x.Dishes, new Dictionary<DateOnly, IReadOnlyList<PlannedDish>>())
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnMove, (DinnerMove m) => moved = m));

        await cut.InvokeAsync(() => cut.Instance.Move("2026-09-29", "2026-10-02"));
        Assert.Equal(new DinnerMove(Tuesday, Monday.AddDays(4)), moved);

        moved = null;
        await cut.InvokeAsync(() => cut.Instance.Move("2026-09-29", "noget"));
        await cut.InvokeAsync(() => cut.Instance.Move("2026-09-29", "2026-09-29"));
        Assert.Null(moved);
    }

    [Fact]
    public void The_picker_lists_the_book_filters_by_category_and_offers_the_typed_text()
    {
        Recipe? picked = null;
        string? typed = null;
        var cut = Render<RecipePickerDialog>(p => p
            .Add(x => x.Day, Tuesday)
            .Add(x => x.Course, DinnerCourse.Main)
            .Add(x => x.Replacing, true)
            .Add(x => x.CurrentRecipeId, 2)
            .Add(x => x.OnPickRecipe, (Recipe r) => picked = r)
            .Add(x => x.OnPickText, (string t) => typed = t));

        Assert.Equal("Hovedret tirsdag 29. september", cut.Find(".hub-dialog__title").TextContent);
        Assert.DoesNotContain(cut.FindAll(".hub-choice__option"), o => o.TextContent.Contains("Hovedret")); // "Skift ret" keeps the course
        Assert.Equal(["Appelsin blondie", "Chili sin carne", "Is med bær"], cut.FindAll(".hub-list__title").Select(e => e.TextContent));
        Assert.Contains("Valgt", cut.FindAll(".hub-list__row")[1].TextContent);

        cut.FindAll(".hub-choice__option").Single(o => o.TextContent.Contains("Kager")).Click();
        Assert.Equal(["Appelsin blondie"], cut.FindAll(".hub-list__title").Select(e => e.TextContent));

        cut.FindAll(".hub-list__row")[0].Click();
        Assert.Equal(23, picked?.Id);

        cut.Find("input").Input("  pizza ude ");
        cut.FindAll(".hub-list__row").Single(r => r.TextContent.Contains("Brug »Pizza ude«")).Click();
        Assert.Equal("Pizza ude", typed);
    }

    [Fact]
    public void Adding_to_a_day_asks_which_course_and_greys_out_the_planned_ones()
    {
        var course = DinnerCourse.Dessert;
        Recipe? picked = null;
        var cut = Render<RecipePickerDialog>(p => p
            .Add(x => x.Day, Tuesday)
            .Add(x => x.Course, course)
            .Add(x => x.CourseChanged, (DinnerCourse c) => course = c)
            .Add(x => x.TakenCourses, [DinnerCourse.Main])
            .Add(x => x.OnPickRecipe, (Recipe r) => picked = r));

        Assert.Equal("Aftensmad tirsdag 29. september", cut.Find(".hub-dialog__title").TextContent);
        var courses = cut.FindAll(".hub-choice--segmented .hub-choice__option").Take(3).ToList();
        Assert.Equal(["Forret", "Hovedret", "Dessert"], courses.Select(e => e.TextContent.Trim()));
        Assert.Equal([false, true, false], courses.Select(e => e.HasAttribute("disabled")));
        Assert.Equal("true", courses[2].GetAttribute("aria-checked"));

        // A dessert starts on the book's "Dessert" category.
        Assert.Equal(["Is med bær"], cut.FindAll(".hub-list__title").Select(e => e.TextContent));

        // A starter: the book has no starters, so all recipes are shown.
        courses[0].Click();
        Assert.Equal(DinnerCourse.Starter, course);
        Assert.Equal(3, cut.FindAll(".hub-list__title").Count);

        cut.FindAll(".hub-list__row")[0].Click();
        Assert.Equal(23, picked?.Id);
    }

    [Fact]
    public void The_dinner_dialog_shows_the_dish()
    {
        var cut = RenderChili();

        Assert.Equal("Chili sin carne", cut.Find(".hub-dialog__title").TextContent);
        Assert.Contains("Hovedret · i dag, tirsdag 29. september", cut.Find(".mp-details__row").TextContent);
        Assert.Equal(["35 min", "4 personer", "Let", "Aftensmad"], cut.FindAll(".mp-details__fact").Select(e => e.TextContent.Trim()));
        Assert.Equal(["2 dåse Bønner", "Topping", "Creme fraiche"], cut.FindAll(".mp-details__ingredients li").Select(e => e.TextContent));
        Assert.Single(cut.FindAll(".mp-details__subheading"));
        Assert.Empty(cut.FindAll(".hub-choice"));
    }

    [Fact]
    public void A_dinner_whose_recipe_is_gone_says_so()
    {
        var cut = Render<DinnerDialog>(p => p
            .Add(x => x.Dish, new PlannedDish(Tuesday, DinnerCourse.Dessert, 99, "Gammel ret"))
            .Add(x => x.Today, Tuesday));

        Assert.Equal("Gammel ret", cut.Find(".hub-dialog__title").TextContent);
        Assert.Contains("Dessert · i dag", cut.Find(".mp-details__row").TextContent);
        Assert.Contains("Opskriften kan ikke vises lige nu", cut.Markup);
        Assert.Empty(cut.FindAll(".mp-details__print"));
    }

    [Fact]
    public void Print_is_only_offered_for_a_recipe_when_the_printer_is_set_up()
    {
        var ownDish = Render<DinnerDialog>(p => p
            .Add(x => x.Dish, new PlannedDish(Tuesday, DinnerCourse.Main, null, "Pizza ude"))
            .Add(x => x.Today, Tuesday));
        Assert.Empty(ownDish.FindAll(".mp-details__print"));

        printOptions.ApiKey = null;
        var notSetUp = RenderChili();
        Assert.Empty(notSetUp.FindAll(".mp-details__print"));
    }

    [Fact]
    public void Print_sends_the_recipe_as_a_pdf_and_says_so()
    {
        var cut = RenderChili();
        var footer = cut.Find(".hub-dialog__footer");
        Assert.Equal(["Print", "Fjern", "Skift ret"], footer.QuerySelectorAll(".hub-btn__label").Select(e => e.TextContent));

        cut.Find(".mp-details__print button").Click();

        cut.WaitForAssertion(() => Assert.Contains(Toasts.Visible, t => t.Title == "Sendt til printeren"));
        var submit = Assert.Single(printServer.Submitted);
        Assert.Equal(7, (int)submit["printerId"]!);
        Assert.False((bool)submit["color"]!);   // no photo → black and white
        var pdf = Convert.FromBase64String((string)submit["documentBase64"]!);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void A_print_that_fails_says_why_in_danish()
    {
        printServer.Offline = true;
        var cut = RenderChili();

        cut.Find(".mp-details__print button").Click();

        cut.WaitForAssertion(() =>
        {
            var toast = Assert.Single(Toasts.Visible);
            Assert.Equal("Opskriften blev ikke udskrevet", toast.Title);
            Assert.Equal("Printserveren kan ikke nås lige nu.", toast.Message);
        });
    }

    [Fact]
    public void Inspiration_swaps_the_book_for_mambeno_and_keeps_the_search()
    {
        var cut = RenderPicker();
        Assert.Equal(["Opskriftsbogen", "Inspiration"], SourceOptions(cut).Select(o => o.TextContent.Trim()));
        Assert.Empty(mambeno.Queries); // the book first – Mambeno is only asked when the family wants inspiration

        SourceOptions(cut)[1].Click();

        // A main course starts on Mambeno's "Aftensmad"; a recipe the book already has is marked.
        cut.WaitForAssertion(() => Assert.Equal(["Chili sin carne", "Laks i ovn", "Pasta med pesto"], cut.FindAll(".hub-list__title").Select(e => e.TextContent)));
        Assert.Equal(18, mambeno.Queries[^1].CategoryId);
        Assert.Contains("I opskriftsbogen", cut.FindAll(".hub-list__row")[0].TextContent);
        Assert.Equal("3 retter", cut.Find(".mp-inspiration__count").TextContent);
        Assert.Contains(cut.FindAll(".mp-inspiration__chips--sub .hub-choice__option"), o => o.TextContent == "Fisk");

        // Typing searches all of Mambeno – the course's category was only a starting point.
        cut.Find("input").Input("laks");
        cut.WaitForAssertion(() => Assert.Equal(["Laks i ovn"], cut.FindAll(".hub-list__title").Select(e => e.TextContent)));
        Assert.Equal(("laks", (int?)null), (mambeno.Queries[^1].Search, mambeno.Queries[^1].CategoryId));

        // Back to the book with the same search.
        SourceOptions(cut)[0].Click();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll(".hub-list__title"), e => e.TextContent == "Brug »Laks«"));
    }

    [Fact]
    public void A_mambeno_recipe_is_previewed_then_copied_into_the_recipe_book()
    {
        Recipe? picked = null;
        var cut = RenderPicker(p => p.Add(x => x.OnPickRecipe, (Recipe r) => picked = r));
        SourceOptions(cut)[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".hub-list__row")));

        cut.FindAll(".hub-list__row").Single(r => r.TextContent.Contains("Laks i ovn")).Click();

        cut.WaitForAssertion(() => Assert.Equal("Laks i ovn", cut.Find(".mp-preview__title").TextContent));
        Assert.Equal(["Laks", "1 kg laks", "Sovs", "½ dl fløde"], cut.FindAll(".mp-preview__ingredients li").Select(e => e.TextContent));
        Assert.Equal(["Bag laksen."], cut.FindAll(".mp-preview__steps li").Select(e => e.TextContent));
        Assert.True(cut.Find(".mp-picker").HasAttribute("hidden"));

        FooterButton(cut, "Vælg retten").Click();

        // Saving runs after the click without another render, so wait for the result itself.
        Assert.True(SpinWait.SpinUntil(() => picked is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(99, picked?.Id);
        var created = Assert.Single(book.Created);
        Assert.Equal("Laks i ovn", (string?)created["title"]);
        Assert.Equal("Mambeno", (string?)created["author"]);
        Assert.Equal(1, (int?)created["categoryId"]); // the book's own "Aftensmad"
        Assert.Equal(["Laks:", "laks", "Sovs:", "fløde"], created["ingredients"]!.AsArray().Select(i => (string?)i!["name"]));
        Assert.Equal("Laks i ovn er gemt i opskriftsbogen", Assert.Single(Toasts.Visible).Title);
    }

    [Fact]
    public void A_mambeno_recipe_the_book_already_has_is_not_copied_again()
    {
        Recipe? picked = null;
        var cut = RenderPicker(p => p.Add(x => x.OnPickRecipe, (Recipe r) => picked = r));
        SourceOptions(cut)[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".hub-list__row")));

        cut.FindAll(".hub-list__row")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("Retten ligger allerede i opskriftsbogen", cut.Find(".mp-preview").TextContent));
        FooterButton(cut, "Tilbage").Click();
        cut.WaitForAssertion(() => Assert.False(cut.Find(".mp-picker").HasAttribute("hidden")));
        Assert.Empty(cut.FindAll(".mp-preview"));

        cut.FindAll(".hub-list__row")[0].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".mp-preview")));
        FooterButton(cut, "Vælg retten").Click();

        Assert.True(SpinWait.SpinUntil(() => picked is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(Chili.Id, picked?.Id);
        Assert.Empty(book.Created);
    }

    [Fact]
    public void Many_mambeno_recipes_come_thirty_at_a_time()
    {
        mambeno.AddMany(70);
        var cut = RenderPicker();
        SourceOptions(cut)[1].Click();
        cut.WaitForAssertion(() => Assert.Equal(30, cut.FindAll(".hub-list__row").Count));
        Assert.Equal("73 retter", cut.Find(".mp-inspiration__count").TextContent);

        cut.Find(".mp-inspiration__more button").Click();
        cut.WaitForAssertion(() => Assert.Equal(60, cut.FindAll(".hub-list__row").Count));
        Assert.Contains("13 tilbage", cut.Find(".mp-inspiration__more button").TextContent);
        Assert.Equal(2, mambeno.Queries[^1].Page);
    }

    [Fact]
    public void When_mambeno_is_down_it_says_so_and_can_try_again()
    {
        mambeno.Offline = true;
        var cut = RenderPicker();
        SourceOptions(cut)[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("Mambeno kan ikke nås lige nu.", cut.Find(".mp-inspiration__results").TextContent));

        mambeno.Offline = false;
        cut.FindAll(".mp-inspiration__results button").Single(b => b.TextContent.Contains("Prøv igen")).Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".hub-list__row").Count));
    }

    [Fact]
    public void Without_mambeno_there_is_no_inspiration()
    {
        mambeno.IsConfigured = false;
        var cut = RenderPicker();

        Assert.Empty(SourceOptions(cut));
        cut.Find("input").Input("lasagne");
        Assert.DoesNotContain(cut.FindAll(".hub-list__title"), e => e.TextContent.Contains("Mambeno"));
    }

    private IRenderedComponent<RecipePickerDialog> RenderPicker(Action<ComponentParameterCollectionBuilder<RecipePickerDialog>>? extra = null) =>
        Render<RecipePickerDialog>(p =>
        {
            p.Add(x => x.Day, Tuesday).Add(x => x.Course, DinnerCourse.Main);
            extra?.Invoke(p);
        });

    private static AngleSharp.Dom.IElement FooterButton(IRenderedComponent<RecipePickerDialog> cut, string text)
    {
        AngleSharp.Dom.IElement? button = null;
        cut.WaitForAssertion(() => button = Assert.Single(cut.FindAll(".hub-dialog__footer button"), b => b.TextContent.Contains(text)));
        return button!;
    }

    private static List<AngleSharp.Dom.IElement> SourceOptions(IRenderedComponent<RecipePickerDialog> cut) =>
        [.. cut.FindAll(".hub-choice__option").Where(o => o.TextContent.Trim() is "Opskriftsbogen" or "Inspiration")];

    private IToastService Toasts => Services.GetRequiredService<IToastService>();

    private IRenderedComponent<DinnerDialog> RenderChili() => Render<DinnerDialog>(p => p
        .Add(x => x.Dish, new PlannedDish(Tuesday, DinnerCourse.Main, 2, "Chili"))
        .Add(x => x.Recipe, Chili)
        .Add(x => x.Today, Tuesday));

    private sealed class FixedOptions(RecipeApiOptions value) : IOptionsMonitor<RecipeApiOptions>
    {
        public RecipeApiOptions CurrentValue => value;

        public RecipeApiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<RecipeApiOptions, string?> listener) => null;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("The components must not call the recipe book.");
    }

    /// <summary>The recipe book's write side: <c>POST /recipes</c> saves as id 99, <c>GET /lookups</c> answers the categories.</summary>
    private sealed class RecipeBookStub : HttpMessageHandler
    {
        public List<JsonObject> Created { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonNode answer;
            if (request.Method == HttpMethod.Post && path.EndsWith("/recipes", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                Created.Add(body);
                var saved = body.DeepClone().AsObject();
                saved["id"] = 99;
                saved["category"] = "Aftensmad";
                answer = saved;
            }
            else if (request.Method == HttpMethod.Get && path.EndsWith("/lookups", StringComparison.Ordinal))
            {
                answer = JsonNode.Parse("""{"difficulties":[],"categories":[{"id":1,"name":"Aftensmad","recipeCount":2},{"id":5,"name":"Dessert","recipeCount":1},{"id":4,"name":"Kager","recipeCount":1}],"units":[],"ingredients":[]}""")!;
            }
            else
            {
                throw new InvalidOperationException($"The components must not call {request.Method} {path}.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Mambeno with three recipes (one the book already has) and a small category tree.</summary>
    private sealed class FakeMambeno : IMambenoService
    {
        private readonly List<MambenoRecipe> recipes =
        [
            new() { Id = 1, Title = "Chili sin carne", CategoryId = 18, Category = "Aftensmad", Categories = [new(18, "Aftensmad")] },
            new()
            {
                Id = 2, Title = "Laks i ovn", CategoryId = 38, Category = "Fisk", Categories = [new(18, "Aftensmad"), new(38, "Fisk")],
                TotalTimeMinutes = 30, Servings = 4, SourceUrl = "https://mambeno.dk/opskrifter/laks/",
                Ingredients =
                [
                    new() { Amount = "1", Quantity = 1, Unit = "kg", Name = "laks", Group = "Laks" },
                    new() { Amount = "0.5", Quantity = 0.5, Unit = "dl", Name = "fløde", Group = "Sovs" },
                ],
                Steps = ["Bag laksen."],
            },
            new() { Id = 3, Title = "Pasta med pesto", CategoryId = 18, Category = "Aftensmad", Categories = [new(18, "Aftensmad")] },
        ];

        public List<MambenoQuery> Queries { get; } = [];

        public bool IsConfigured { get; set; } = true;

        public bool Offline { get; set; }

        public void AddMany(int count) =>
            recipes.AddRange(Enumerable.Range(100, count).Select(i => new MambenoRecipe { Id = i, Title = $"Ret {i}", CategoryId = 18, Categories = [new(18, "Aftensmad")] }));

        public Task<IReadOnlyList<MambenoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<MambenoCategory>>(
            [
                new() { Id = 18, Name = "Aftensmad", RecipeCount = 3206 },
                new() { Id = 38, Name = "Fisk", RecipeCount = 501, ParentId = 18 },
                new() { Id = 14, Name = "Kager, bagværk og sødt", RecipeCount = 341 },
            ]);

        public Task<MambenoCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MambenoRecipePage> SearchRecipesAsync(MambenoQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var found = recipes
                .Where(r => string.IsNullOrEmpty(query.Search) || r.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
                .Where(r => query.CategoryId is not { } id || r.Categories.Any(c => c.Id == id))
                .ToList();
            return Answer(new MambenoRecipePage
            {
                Items = [.. found.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)],
                TotalCount = found.Count,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling(found.Count / (double)query.PageSize),
            });
        }

        public Task<MambenoRecipe?> GetRecipeAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MambenoStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private Task<T> Answer<T>(T value) =>
            Offline ? Task.FromException<T>(new MambenoException(MambenoError.Offline, "Mambeno kan ikke nås lige nu.")) : Task.FromResult(value);
    }

    private sealed class FixedPrintOptions(PrintApiOptions value) : IOptionsMonitor<PrintApiOptions>
    {
        public PrintApiOptions CurrentValue => value;

        public PrintApiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PrintApiOptions, string?> listener) => null;
    }

    private sealed class PrintHttp(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Accepts every print job, like the print server's <c>POST /Print/Submit</c>.</summary>
    private sealed class PrintServerStub : HttpMessageHandler
    {
        public List<JsonNode> Submitted { get; } = [];

        public bool Offline { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            Assert.Equal("/Print/Submit", request.RequestUri!.AbsolutePath);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Submitted.Add(body);
            var job = new JsonObject
            {
                ["id"] = Guid.NewGuid(),
                ["printerId"] = (int)body["printerId"]!,
                ["printerName"] = "HP",
                ["status"] = "Queued",
                ["pageCount"] = 1,
                ["pagesToPrint"] = 1,
                ["copies"] = 1,
                ["createdAt"] = "2026-10-01T08:00:00",
                ["updatedAt"] = "2026-10-01T08:00:00",
            };
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(job.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
