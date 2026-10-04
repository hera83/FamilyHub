using System.Globalization;
using Bunit;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Dexcom;
using FamilyHub.Core.Notifications;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Dexcom.Glucose;
using FamilyHub.Modules.Dexcom.Pages;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Dexcom;

public sealed class DexcomPageTests : BunitContext
{
    /// <summary>21:30 in Copenhagen.</summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 19, 30, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(Start);
    private readonly GlucoseMonitorTests.FakeDexcom api = new();
    private readonly HubClock clock;
    private readonly GlucoseMonitor monitor;

    public DexcomPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        monitor = new GlucoseMonitor(api, new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory })), clock, NullLogger<GlucoseMonitor>.Instance);

        Services.AddLogging();
        Services.AddSingleton<IToastService>(new ToastService(TimeProvider.System, NullLogger<ToastService>.Instance));
        Services.AddSingleton<IHubClock>(clock);
        Services.AddSingleton(monitor);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        clock.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task WithReadingsAsync(double latest, double minutesOld = 2, GlucoseTrend trend = GlucoseTrend.FortyFiveUp)
    {
        api.AddEvery5Minutes(Start.AddHours(-6), Start.AddMinutes(-10), 6.0);
        api.Add(Start.AddMinutes(-minutesOld), latest, trend);
        await monitor.RefreshAsync();
    }

    [Fact]
    public async Task The_reading_now_is_shown_big_with_the_trend_and_how_old_it_is()
    {
        await WithReadingsAsync(8.5);

        var cut = Render<DexcomPage>();

        Assert.Equal("8,5", cut.Find(".dx-now__value").TextContent);
        Assert.Equal("Stiger", cut.Find(".dx-trend").GetAttribute("aria-label"));
        Assert.Contains("I målområdet", cut.Find(".dx-now__status").TextContent);
        Assert.Contains("Målt 21:28 · 2 min siden", cut.Find(".dx-now__age").TextContent);
        Assert.NotNull(cut.Find(".dx-now--in-range"));
    }

    [Theory]
    [InlineData(3.2, "low", "Lav")]
    [InlineData(13.4, "high", "Høj")]
    public async Task Low_and_high_get_colour_together_with_a_word(double mmolL, string state, string word)
    {
        await WithReadingsAsync(mmolL);

        var cut = Render<DexcomPage>();

        Assert.NotNull(cut.Find($".dx-now--{state}"));
        Assert.Contains(word, cut.Find(".dx-now__status").TextContent);
    }

    [Fact]
    public async Task After_8_minutes_the_number_gives_way_to_a_dash()
    {
        await WithReadingsAsync(8.5, minutesOld: 2);
        var cut = Render<DexcomPage>();

        time.Advance(TimeSpan.FromMinutes(7));
        cut.WaitForAssertion(() => Assert.Equal("–", cut.Find(".dx-now__value").TextContent));

        Assert.NotNull(cut.Find(".dx-now--missing"));
        Assert.Contains("Ingen ny måling i 9 min", cut.Find(".dx-now__status").TextContent);
        Assert.Contains("Seneste: 8,5 mmol/L 21:28", cut.Find(".dx-now__age").TextContent);
        Assert.Empty(cut.FindAll(".dx-trend"));
    }

    [Fact]
    public async Task The_graph_follows_the_chosen_period_and_starts_with_the_familys_choice()
    {
        await monitor.SaveSettingsAsync(new GlucoseSettings { ChartHours = 3 });
        await WithReadingsAsync(8.5);
        var cut = Render<DexcomPage>();

        Assert.Equal("Sidste 3 timer", cut.Find(".dx-graph .hub-card__title").TextContent);
        Assert.Equal(["19:00", "19:30", "20:00", "20:30", "21:00"], cut.FindAll(".dx-chart__times .dx-chart__label").Select(e => e.TextContent));

        cut.FindAll(".dx-graph .hub-choice__option").Single(b => b.TextContent.Trim() == "6 t").Click();

        Assert.Equal("Sidste 6 timer", cut.Find(".dx-graph .hub-card__title").TextContent);
        Assert.Contains("laveste 6,0, højeste 8,5", cut.Find(".dx-chart").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Time_in_range_shows_the_share_and_says_how_much_data_it_is_based_on()
    {
        await WithReadingsAsync(12.0);

        var cut = Render<DexcomPage>();

        Assert.Equal("99 %", cut.Find(".dx-tir__percent").TextContent);
        Assert.Contains("3,9–10,0 mmol/L", cut.Find(".dx-tir__caption").TextContent);
        Assert.Contains("Bygger kun på det seneste døgns målinger", cut.Find(".dx-tir__note").TextContent);
        Assert.Equal(2, cut.FindAll(".dx-tir__bar .dx-tir__segment").Count);
    }

    [Fact]
    public void Not_configured_explains_what_is_missing()
    {
        api.Configured = false;

        var cut = Render<DexcomPage>();

        Assert.Contains("Blodsukker er ikke sat op endnu", cut.Markup);
        Assert.Contains("DEXCOM_API_KEY", cut.Markup);
    }

    [Fact]
    public async Task A_rejected_key_is_shown_where_the_readings_are()
    {
        await WithReadingsAsync(8.5);
        api.Failure = DexcomError.Unauthorized;
        await monitor.RefreshAsync();

        var cut = Render<DexcomPage>();

        Assert.Contains("Blodsukker-API'et afviste nøglen", cut.Find(".hub-infobox").TextContent);
    }

    [Fact]
    public void The_target_range_is_changed_with_steppers_and_saved_for_every_screen()
    {
        var cut = Render<DexcomSettingsPage>();

        // One step up on the low limit (keyboard activation: exactly one step, no hold-to-repeat).
        cut.FindAll("button[aria-label='Højere']")[0].Click(new MouseEventArgs { Detail = 0 });

        cut.WaitForAssertion(() => Assert.Equal(4.0m, monitor.Settings.LowMmolL));
        Assert.Contains("4,0–10,0 mmol/L", cut.Find(".dx-settings__footer").TextContent);

        // The file is written just after the screen changes – a restarted Family Hub reads the new limit.
        GlucoseMonitor Restarted() => new(api, new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory })), clock, NullLogger<GlucoseMonitor>.Instance);
        Assert.True(SpinWait.SpinUntil(() => Restarted().Settings.LowMmolL == 4.0m, TimeSpan.FromSeconds(5)));
    }
}
