using FamilyHub.Core.Configuration;
using FamilyHub.Core.Dexcom;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Dexcom.Glucose;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Dexcom;

public sealed class GlucoseMonitorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 19, 30, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(Start);
    private readonly HubClock clock;
    private readonly FakeDexcom api = new();

    public GlucoseMonitorTests()
    {
        clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
    }

    public void Dispose()
    {
        clock.Dispose();
        Directory.Delete(directory, recursive: true);
    }

    private GlucoseMonitor Create() =>
        new(api, new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory })), clock, NullLogger<GlucoseMonitor>.Instance);

    [Fact]
    public async Task The_first_round_fetches_the_last_day_first_and_then_90_days_back()
    {
        api.AddEvery5Minutes(Start.AddDays(-40), Start, 6.0);
        var monitor = Create();
        var changes = 0;
        monitor.Changed += () => changes++;

        await monitor.RefreshAsync();

        Assert.Equal(TimeSpan.FromDays(1), api.Calls[0].To - api.Calls[0].From - TimeSpan.FromMinutes(5));
        Assert.Equal(Start.AddDays(-90), api.Calls.Min(c => c.From));
        Assert.True(monitor.Status.HistoryLoaded);
        Assert.Equal(GlucoseProblem.None, monitor.Status.Problem);
        Assert.Equal(40 * 288 + 1, monitor.GetReadings(Start.AddDays(-90), Start).Count);
        Assert.Equal(Start, monitor.Latest!.Time);
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task After_that_it_fetches_3_hours_and_the_whole_day_every_half_hour()
    {
        var monitor = Create();
        await monitor.RefreshAsync();
        api.Calls.Clear();

        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync();
        time.Advance(TimeSpan.FromMinutes(30));
        await monitor.RefreshAsync();

        Assert.Equal(TimeSpan.FromHours(3), api.Calls[0].To - api.Calls[0].From - TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromDays(1), api.Calls[1].To - api.Calls[1].From - TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_new_reading_shows_up_and_is_announced()
    {
        api.AddEvery5Minutes(Start.AddHours(-1), Start, 6.0);
        var monitor = Create();
        await monitor.RefreshAsync();
        var changes = 0;
        monitor.Changed += () => changes++;

        time.Advance(TimeSpan.FromMinutes(5));
        api.Add(Start.AddMinutes(5), 6.4);
        await monitor.RefreshAsync();
        await monitor.RefreshAsync();

        Assert.Equal(6.4, monitor.Latest!.MmolL);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(DexcomError.Offline, GlucoseProblem.Offline)]
    [InlineData(DexcomError.Unauthorized, GlucoseProblem.Unauthorized)]
    [InlineData(DexcomError.Failed, GlucoseProblem.Failed)]
    public async Task A_failure_is_kept_in_the_status_and_the_readings_stay(DexcomError error, GlucoseProblem problem)
    {
        api.AddEvery5Minutes(Start.AddHours(-1), Start, 6.0);
        var monitor = Create();
        await monitor.RefreshAsync();

        api.Failure = error;
        time.Advance(TimeSpan.FromMinutes(1));
        await monitor.RefreshAsync();

        Assert.Equal(problem, monitor.Status.Problem);
        Assert.Equal(Start, monitor.Status.LastSuccess);
        Assert.Equal(Start.AddMinutes(1), monitor.Status.LastAttempt);
        Assert.Equal(13, monitor.GetReadings(Start.AddHours(-2), Start).Count);

        api.Failure = null;
        await monitor.RefreshAsync();
        Assert.Equal(GlucoseProblem.None, monitor.Status.Problem);
    }

    [Fact]
    public async Task If_the_history_fails_halfway_it_is_tried_again_next_round()
    {
        api.AddEvery5Minutes(Start.AddDays(-3), Start, 6.0);
        api.FailBefore = Start.AddDays(-1).AddMinutes(-1);
        var monitor = Create();

        await monitor.RefreshAsync();

        Assert.True(monitor.HasData);
        Assert.False(monitor.Status.HistoryLoaded);
        Assert.Equal(GlucoseProblem.Failed, monitor.Status.Problem);

        api.FailBefore = null;
        await monitor.RefreshAsync();

        Assert.True(monitor.Status.HistoryLoaded);
        Assert.Equal(3 * 288 + 1, monitor.GetReadings(Start.AddDays(-90), Start).Count);
    }

    [Fact]
    public async Task Not_configured_means_no_calls()
    {
        api.Configured = false;
        var monitor = Create();

        await monitor.RefreshAsync();

        Assert.False(monitor.IsConfigured);
        Assert.Equal(GlucoseProblem.NotConfigured, monitor.Status.Problem);
        Assert.Empty(api.Calls);
        Assert.False(monitor.HasData);
    }

    [Fact]
    public async Task Settings_are_saved_for_every_screen_and_survive_a_restart()
    {
        var monitor = Create();
        var changes = 0;
        monitor.Changed += () => changes++;

        await monitor.SaveSettingsAsync(new GlucoseSettings { LowMmolL = 4.0m, HighMmolL = 8.5m, ChartHours = 3 });

        Assert.Equal(1, changes);
        var restarted = Create();
        Assert.Equal(new GlucoseSettings { LowMmolL = 4.0m, HighMmolL = 8.5m, ChartHours = 3 }, restarted.Settings);
    }

    [Fact]
    public void Without_a_file_the_usual_target_range_is_used()
    {
        Assert.Equal(new GlucoseSettings(), Create().Settings);
        Assert.Equal(3.9m, Create().Settings.LowMmolL);
    }

    /// <summary>Answers like the real API: every reading between From and To.</summary>
    internal sealed class FakeDexcom : IDexcomService
    {
        private readonly List<GlucoseReading> readings = [];

        public List<(DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];

        public bool Configured { get; set; } = true;

        public DexcomError? Failure { get; set; }

        /// <summary>Fail calls that reach further back than this – a history that breaks halfway.</summary>
        public DateTimeOffset? FailBefore { get; set; }

        public bool IsConfigured => Configured;

        public void Add(DateTimeOffset time, double mmolL, GlucoseTrend trend = GlucoseTrend.Flat) =>
            readings.Add(new GlucoseReading { Time = time, MmolL = mmolL, Trend = trend });

        public void AddEvery5Minutes(DateTimeOffset from, DateTimeOffset to, double mmolL)
        {
            for (var t = from; t <= to; t += TimeSpan.FromMinutes(5))
            {
                Add(t, mmolL);
            }
        }

        public Task<GlucoseReading> GetLatestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(readings.MaxBy(r => r.Time) ?? throw new DexcomException(DexcomError.NoData, "Der er ingen målinger endnu."));

        public Task<GlucoseReadings> GetReadingsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Calls.Add((from, to));
            var failure = Failure ?? (FailBefore is { } before && from < before ? DexcomError.Failed : null);
            if (failure is { } error)
            {
                throw new DexcomException(error, "Fejl");
            }

            return Task.FromResult(new GlucoseReadings
            {
                From = from,
                To = to,
                Items = [.. readings.Where(r => r.Time >= from && r.Time <= to).OrderBy(r => r.Time)],
            });
        }

        public Task<DexcomStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DexcomStatus { IsHealthy = true, Status = "Healthy", CheckedAt = DateTimeOffset.UtcNow });
    }
}
