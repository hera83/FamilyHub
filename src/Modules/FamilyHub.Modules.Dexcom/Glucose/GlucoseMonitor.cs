using FamilyHub.Core.Dexcom;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>Why the readings could not be fetched – shown with an InfoBox, never with toasts.</summary>
public enum GlucoseProblem
{
    None,

    /// <summary>No address or key (DEXCOM_API_KEY in .env).</summary>
    NotConfigured,

    /// <summary>The API could not be reached.</summary>
    Offline,

    /// <summary>The API rejected the key.</summary>
    Unauthorized,

    /// <summary>Anything else; details are in the log.</summary>
    Failed,
}

/// <summary>How fresh the readings in memory are.</summary>
public sealed record GlucoseSyncStatus
{
    public static GlucoseSyncStatus Never { get; } = new();

    public DateTimeOffset? LastSuccess { get; init; }

    public DateTimeOffset? LastAttempt { get; init; }

    public GlucoseProblem Problem { get; init; }

    /// <summary>True once the whole history (90 days) has been fetched – until then time in range is not complete.</summary>
    public bool HistoryLoaded { get; init; }
}

/// <summary>
/// The glucose readings for every screen: one copy in memory (90 days ≈ 26.000 readings), kept fresh by
/// <see cref="GlucoseSyncWorker"/> once a minute – so ten open screens still make one call a minute.
/// Also owns the family's settings (target range). Screens subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class GlucoseMonitor
{
    /// <summary>Time in range can be shown for up to 90 days ("3 måneder").</summary>
    public const int HistoryDays = 90;

    /// <summary>Fetched every minute: new readings, and readings Dexcom delivers late after a short signal loss.</summary>
    internal static readonly TimeSpan RecentWindow = TimeSpan.FromHours(3);

    /// <summary>The last day is fetched again every half hour – Dexcom can fill gaps of several hours.</summary>
    internal static readonly TimeSpan DayWindow = TimeSpan.FromDays(1);

    internal static readonly TimeSpan DayRefreshInterval = TimeSpan.FromMinutes(30);

    /// <summary>The history is fetched in pieces, newest first, so the page fills before the old days arrive.</summary>
    internal static readonly TimeSpan HistoryChunk = TimeSpan.FromDays(30);

    private readonly IDexcomService api;
    private readonly IHubClock clock;
    private readonly ILogger<GlucoseMonitor> logger;
    private readonly JsonFileStore<GlucoseSettings> settingsStore;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private GlucoseReading[] readings = [];
    private DateTimeOffset? lastDayRefresh;

    public GlucoseMonitor(IDexcomService api, IAppDataPaths paths, IHubClock clock, ILogger<GlucoseMonitor> logger)
    {
        this.api = api;
        this.clock = clock;
        this.logger = logger;
        settingsStore = new JsonFileStore<GlucoseSettings>(paths.GetFilePath("dexcom/indstillinger.json"), logger);
        Settings = (settingsStore.Load() ?? new GlucoseSettings()).Normalized();
    }

    /// <summary>Raised from a background thread – components use <c>InvokeAsync(StateHasChanged)</c>.</summary>
    public event Action? Changed;

    public bool IsConfigured => api.IsConfigured;

    public GlucoseSettings Settings { get; private set; }

    public GlucoseSyncStatus Status { get; private set; } = GlucoseSyncStatus.Never;

    /// <summary>The newest reading in memory – check <see cref="GlucoseRules.IsFresh"/> before showing it as "now".</summary>
    public GlucoseReading? Latest => readings is [.., var last] ? last : null;

    /// <summary>True once the first readings have arrived (the page shows a skeleton until then).</summary>
    public bool HasData => Status.LastSuccess is not null;

    /// <summary>The readings in the period, oldest first.</summary>
    public IReadOnlyList<GlucoseReading> GetReadings(DateTimeOffset from, DateTimeOffset to) => GlucoseHistory.Slice(readings, from, to);

    public async Task SaveSettingsAsync(GlucoseSettings settings, CancellationToken cancellationToken = default)
    {
        Settings = settings.Normalized();
        Changed?.Invoke();
        await settingsStore.SaveAsync(Settings, cancellationToken);
    }

    /// <summary>
    /// One round: the whole history the first time (and until it succeeds), otherwise the last 3 hours –
    /// or the last day every half hour. Failures are kept in <see cref="Status"/>, not thrown.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var now = clock.Now;
            if (!api.IsConfigured)
            {
                SetStatus(Status with { LastAttempt = now, Problem = GlucoseProblem.NotConfigured });
                return;
            }

            try
            {
                if (!Status.HistoryLoaded)
                {
                    await LoadHistoryAsync(now, cancellationToken);
                }
                else if (lastDayRefresh is not { } last || now - last >= DayRefreshInterval)
                {
                    await FetchAsync(now - DayWindow, now, now, cancellationToken);
                    lastDayRefresh = now;
                }
                else
                {
                    await FetchAsync(now - RecentWindow, now, now, cancellationToken);
                }

                SetStatus(Status with { LastAttempt = now, LastSuccess = now, Problem = GlucoseProblem.None, HistoryLoaded = true });
            }
            catch (DexcomException ex)
            {
                logger.Log(ex.Error is DexcomError.Offline ? LogLevel.Information : LogLevel.Warning, ex, "Could not fetch glucose readings");
                SetStatus(Status with { LastAttempt = now, Problem = ToProblem(ex.Error) });
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task LoadHistoryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The last day first, so the page has something to show at once.
        await FetchAsync(now - DayWindow, now, now, cancellationToken);
        SetStatus(Status with { LastSuccess = now, Problem = GlucoseProblem.None });

        var oldest = now - TimeSpan.FromDays(HistoryDays);
        for (var to = now - DayWindow; to > oldest; to -= HistoryChunk)
        {
            var from = to - HistoryChunk < oldest ? oldest : to - HistoryChunk;
            await FetchAsync(from, to, now, cancellationToken);
        }

        lastDayRefresh = now;
    }

    private async Task FetchAsync(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A little past "now", so a reading stamped a moment ahead of our clock is not missed.
        var until = to >= now ? now + TimeSpan.FromMinutes(5) : to;
        var fetched = await api.GetReadingsAsync(from, until, cancellationToken);

        var merged = GlucoseHistory.Merge(readings, from, until, fetched.Items, now - TimeSpan.FromDays(HistoryDays));
        if (!merged.AsSpan().SequenceEqual(readings))
        {
            readings = merged;
            Changed?.Invoke();
        }
    }

    private void SetStatus(GlucoseSyncStatus status)
    {
        var changed = status.Problem != Status.Problem || status.HistoryLoaded != Status.HistoryLoaded || (Status.LastSuccess is null) != (status.LastSuccess is null);
        Status = status;
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private static GlucoseProblem ToProblem(DexcomError error) => error switch
    {
        DexcomError.NotConfigured => GlucoseProblem.NotConfigured,
        DexcomError.Offline => GlucoseProblem.Offline,
        DexcomError.Unauthorized => GlucoseProblem.Unauthorized,
        _ => GlucoseProblem.Failed,
    };
}
