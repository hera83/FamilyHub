using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            Recipes = [Blondie, Chili],
            Lookups = new RecipeLookups { Categories = [new() { Id = 1, Name = "Aftensmad", RecipeCount = 1 }, new() { Id = 4, Name = "Kager", RecipeCount = 1 }] },
            LastSuccess = DateTimeOffset.UnixEpoch,
        };
        File.WriteAllText(paths.GetFilePath("madplan/opskrifter.json"), JsonSerializer.Serialize(snapshot, HubJson.Files));

        var api = new RecipeApiClient(new NoHttp(), new FixedOptions(new RecipeApiOptions { ApiKey = "key" }), NullLogger<RecipeApiClient>.Instance);
        var clock = new HubClock(TimeProvider.System, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton(new RecipeService(api, paths, clock, NullLogger<RecipeService>.Instance));

        // The printer: a print server that accepts everything (printer 7 is chosen in the settings).
        var printApi = new PrintApiClient(new PrintHttp(printServer), new FixedPrintOptions(printOptions), NullLogger<PrintApiClient>.Instance);
        var printing = new PrintService(printApi, new FixedPrintOptions(printOptions), TimeProvider.System, NullLogger<PrintService>.Instance);
        Services.AddSingleton(printing);
        Services.AddSingleton(new RecipePrinter(printing, new NoHttp(), clock, NullLogger<RecipePrinter>.Instance));
    }

    private readonly PrintApiOptions printOptions = new() { BaseUrl = "http://print.test:8080", ApiKey = "ak_test", PrinterId = 7 };
    private readonly PrintServerStub printServer = new();

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
        DateOnly? tapped = null;
        var dinners = new Dictionary<DateOnly, PlannedDinner>
        {
            [Tuesday] = new(Tuesday, 2, "Chili"),
            [Monday.AddDays(3)] = new(Monday.AddDays(3), null, "Rester"),
        };

        var cut = Render<MealPlanWeek>(p => p
            .Add(x => x.Days, Week)
            .Add(x => x.Dinners, dinners)
            .Add(x => x.FindRecipe, id => id == 2 ? Chili : null)
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnDaySelected, (DateOnly d) => tapped = d));

        Assert.Equal(7, cut.FindAll(".mp-day").Count);
        Assert.Contains("mp-day--today", cut.Find($".mp-day[data-day='2026-09-29']").ClassName);
        Assert.Equal("Chili sin carne", cut.Find(".mp-day--today .mp-dinner__title").TextContent); // the recipe's current title
        Assert.Equal("35 min · Aftensmad", cut.Find(".mp-day--today .mp-dinner__meta").TextContent);
        Assert.Equal("Egen ret", cut.Find(".mp-day[data-day='2026-10-01'] .mp-dinner__meta").TextContent);
        Assert.Empty(cut.FindAll(".mp-day[data-day='2026-09-28'] .mp-day__empty")); // past days don't invite planning
        Assert.Equal(4, cut.FindAll(".mp-day__empty").Count);

        cut.Find(".mp-day[data-day='2026-10-02'] button").Click();
        Assert.Equal(Monday.AddDays(4), tapped);
    }

    [Fact]
    public async Task A_drop_from_the_browser_becomes_a_move()
    {
        DinnerMove? moved = null;
        var cut = Render<MealPlanWeek>(p => p
            .Add(x => x.Days, Week)
            .Add(x => x.Dinners, new Dictionary<DateOnly, PlannedDinner>())
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnMove, (DinnerMove m) => moved = m));

        await cut.InvokeAsync(() => cut.Instance.Move("2026-09-29", "2026-10-02"));
        Assert.Equal(new DinnerMove(Tuesday, Monday.AddDays(4)), moved);

        moved = null;
        await cut.InvokeAsync(() => cut.Instance.Move("2026-09-29", "noget"));
        Assert.Null(moved);
    }

    [Fact]
    public void The_picker_lists_the_book_filters_by_category_and_offers_the_typed_text()
    {
        Recipe? picked = null;
        string? typed = null;
        var cut = Render<RecipePickerDialog>(p => p
            .Add(x => x.Day, Tuesday)
            .Add(x => x.CurrentRecipeId, 2)
            .Add(x => x.OnPickRecipe, (Recipe r) => picked = r)
            .Add(x => x.OnPickText, (string t) => typed = t));

        Assert.Equal("Aftensmad tirsdag 29. september", cut.Find(".hub-dialog__title").TextContent);
        Assert.Equal(["Appelsin blondie", "Chili sin carne"], cut.FindAll(".hub-list__title").Select(e => e.TextContent));
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
    public void The_dinner_dialog_shows_the_dish_first_and_can_move_it()
    {
        DateOnly? movedTo = null;
        var cut = Render<DinnerDialog>(p => p
            .Add(x => x.Dinner, new PlannedDinner(Tuesday, 2, "Chili"))
            .Add(x => x.Recipe, Chili)
            .Add(x => x.Days, Week)
            .Add(x => x.Today, Tuesday)
            .Add(x => x.OnMove, (DateOnly d) => movedTo = d));

        Assert.Equal("Chili sin carne", cut.Find(".hub-dialog__title").TextContent);
        Assert.Contains("i dag, tirsdag 29. september", cut.Find(".mp-details__row").TextContent);
        Assert.Equal(["35 min", "4 personer", "Let", "Aftensmad"], cut.FindAll(".mp-details__fact").Select(e => e.TextContent.Trim()));
        Assert.Equal(["2 dåse Bønner", "Topping", "Creme fraiche"], cut.FindAll(".mp-details__ingredients li").Select(e => e.TextContent));
        Assert.Single(cut.FindAll(".mp-details__subheading"));

        cut.FindAll(".hub-choice__option").Single(o => o.TextContent == "Fre 2.").Click();
        Assert.Equal(Monday.AddDays(4), movedTo);
    }

    [Fact]
    public void A_dinner_whose_recipe_is_gone_says_so()
    {
        var cut = Render<DinnerDialog>(p => p
            .Add(x => x.Dinner, new PlannedDinner(Tuesday, 99, "Gammel ret"))
            .Add(x => x.Days, Week)
            .Add(x => x.Today, Tuesday));

        Assert.Equal("Gammel ret", cut.Find(".hub-dialog__title").TextContent);
        Assert.Contains("Opskriften kan ikke vises lige nu", cut.Markup);
        Assert.Empty(cut.FindAll(".mp-details__print"));
    }

    [Fact]
    public void Print_is_only_offered_for_a_recipe_when_the_printer_is_set_up()
    {
        var ownDish = Render<DinnerDialog>(p => p
            .Add(x => x.Dinner, new PlannedDinner(Tuesday, null, "Pizza ude"))
            .Add(x => x.Days, Week)
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

    private IToastService Toasts => Services.GetRequiredService<IToastService>();

    private IRenderedComponent<DinnerDialog> RenderChili() => Render<DinnerDialog>(p => p
        .Add(x => x.Dinner, new PlannedDinner(Tuesday, 2, "Chili"))
        .Add(x => x.Recipe, Chili)
        .Add(x => x.Days, Week)
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
