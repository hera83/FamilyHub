using System.Globalization;
using Bunit;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Components;
using FamilyHub.Modules.MealPlan.Plan;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.MealPlan;

public sealed class TonightWidgetTests : BunitContext
{
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly DateOnly Wednesday = Tuesday.AddDays(1);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(Copenhagen(Tuesday, 18, 58));
    private readonly MealPlanService mealPlan;
    private readonly HubClock clock;

    public TonightWidgetTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Directory.CreateDirectory(directory);

        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        var db = new DbContextOptionsBuilder<MealPlanDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "madplan.db")}").Options;
        mealPlan = new MealPlanService(new Factory(db), clock, NullLogger<MealPlanService>.Instance);

        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var api = new RecipeApiClient(new NoHttp(), new FixedOptions(), NullLogger<RecipeApiClient>.Instance);

        Services.AddLogging();
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton(mealPlan);
        Services.AddSingleton(new RecipeService(api, paths, clock, NullLogger<RecipeService>.Instance));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        clock.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }

    /// <summary>A time in Copenhagen (CEST, UTC+2, on these dates) – as UTC, like a real TimeProvider gives it.</summary>
    private static DateTimeOffset Copenhagen(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(2)).ToUniversalTime();

    private void At(DateOnly date, int hour, int minute = 0) => time.SetUtcNow(Copenhagen(date, hour, minute));

    [Theory]
    [InlineData(5, 59, false)]
    [InlineData(6, 0, true)]
    [InlineData(18, 59, true)]
    [InlineData(19, 0, false)]
    [InlineData(23, 30, false)]
    public void Tonight_shows_from_six_in_the_morning_until_seven_in_the_evening(int hour, int minute, bool shown)
    {
        Assert.Equal(shown, MealPlanRules.ShowTonight(new TimeOnly(hour, minute)));
    }

    [Fact]
    public async Task The_card_shows_todays_dinner_during_the_day()
    {
        At(Wednesday, 12);
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Main, "Rester");
        await mealPlan.PlanTextAsync(Wednesday.AddDays(1), DinnerCourse.Main, "Pizza ude");

        var cut = Render<TonightWidget>();

        cut.WaitForAssertion(() => Assert.Equal("Rester", cut.Find(".mp-tonight__title").TextContent));
        Assert.Equal("madplan", cut.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task The_card_is_gone_after_dinner_and_back_with_the_next_dinner_in_the_morning()
    {
        // The clock starts Tuesday 18:58 and runs through the night to Wednesday morning.
        await mealPlan.PlanTextAsync(Tuesday, DinnerCourse.Main, "Rester");
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Main, "Pizza ude");
        var cut = Render<TonightWidget>();
        cut.WaitForAssertion(() => Assert.Equal("Rester", cut.Find(".mp-tonight__title").TextContent));

        RunUntil(Tuesday, 18, 59);
        Assert.Single(cut.FindAll(".mp-tonight"));

        RunUntil(Tuesday, 19, 0);
        cut.WaitForAssertion(() => Assert.Empty(cut.Markup.Trim()));

        RunUntil(Wednesday, 5, 59);
        cut.WaitForAssertion(() => Assert.Empty(cut.Markup.Trim()));

        RunUntil(Wednesday, 6, 0);
        cut.WaitForAssertion(() => Assert.Equal("Pizza ude", cut.Find(".mp-tonight__title").TextContent));
    }

    [Fact]
    public async Task The_main_course_is_the_headline_and_a_starter_and_dessert_are_listed_under_it()
    {
        At(Wednesday, 12);
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Dessert, "Is med bær");
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Main, "Lasagne");
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Starter, "Tomatsuppe");

        var cut = Render<TonightWidget>();

        cut.WaitForAssertion(() => Assert.Equal("Lasagne", cut.Find(".mp-tonight__title").TextContent));
        Assert.Equal(["Forret Tomatsuppe", "Dessert Is med bær"],
            cut.FindAll(".mp-tonight__courses li").Select(li => string.Join(' ', li.Children.Select(c => c.TextContent))));
    }

    [Fact]
    public async Task Only_a_dessert_planned_still_shows_tonight()
    {
        At(Wednesday, 12);
        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Dessert, "Is med bær");

        var cut = Render<TonightWidget>();

        cut.WaitForAssertion(() => Assert.Equal("Is med bær", cut.Find(".mp-tonight__title").TextContent));
        Assert.Empty(cut.FindAll(".mp-tonight__courses"));
    }

    [Fact]
    public void Without_a_dinner_the_home_screen_gets_no_card()
    {
        At(Wednesday, 12);

        var cut = Render<TonightWidget>();

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public async Task A_dinner_planned_on_another_screen_shows_up_right_away()
    {
        At(Wednesday, 12);
        var cut = Render<TonightWidget>();

        await mealPlan.PlanTextAsync(Wednesday, DinnerCourse.Main, "Rester");

        cut.WaitForAssertion(() => Assert.Equal("Rester", cut.Find(".mp-tonight__title").TextContent));
    }

    /// <summary>Lets the clock run a minute at a time until just after <paramref name="hour"/>:<paramref name="minute"/>.</summary>
    private void RunUntil(DateOnly date, int hour, int minute)
    {
        var target = Copenhagen(date, hour, minute).AddSeconds(1);
        while (time.GetUtcNow() < target)
        {
            var left = target - time.GetUtcNow();
            time.Advance(left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1));
        }
    }

    private sealed class Factory(DbContextOptions<MealPlanDbContext> options) : IDbContextFactory<MealPlanDbContext>
    {
        public MealPlanDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedOptions : IOptionsMonitor<RecipeApiOptions>
    {
        public RecipeApiOptions CurrentValue { get; } = new();

        public RecipeApiOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<RecipeApiOptions, string?> listener) => null;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No calls to the recipe book in these tests.");
    }
}
