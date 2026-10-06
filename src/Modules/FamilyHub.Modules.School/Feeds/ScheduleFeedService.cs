using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Schedules;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.School.Feeds;

/// <summary>A lesson from the school's calendar, in the household's own time (Copenhagen).</summary>
/// <param name="Key">The feed's key for the lesson (see <see cref="IcsOccurrence.Key"/>) – null in caches from before it existed.</param>
/// <param name="Description">The calendar's description – shown when the lesson is tapped (<see cref="LessonDetails"/>).</param>
public sealed record FeedEvent(DateTime Start, DateTime End, string Title, string? Location, string? Key = null, string? Description = null);

/// <summary>Why a calendar link could not be read – shown with an InfoBox or under the field, never as a toast.</summary>
public enum FeedProblem
{
    None,

    /// <summary>The school's server could not be reached (or answered too slowly).</summary>
    Offline,

    /// <summary>The server refused or didn't know the link – usually a link that has expired.</summary>
    Rejected,

    /// <summary>The answer wasn't a calendar – e.g. a login page.</summary>
    NotCalendar,

    /// <summary>Not an http(s) or webcal address.</summary>
    InvalidLink,
}

public sealed class FeedException(FeedProblem problem, string message, Exception? inner = null) : Exception(message, inner)
{
    public FeedProblem Problem { get; } = problem;
}

/// <summary>How fresh one schedule's calendar is.</summary>
public sealed record FeedStatus
{
    public static FeedStatus Never { get; } = new();

    public DateTimeOffset? LastSuccess { get; init; }

    public DateTimeOffset? LastAttempt { get; init; }

    public FeedProblem Problem { get; init; }
}

/// <summary>
/// Fetches the calendar links of gymnasium and university schedules (see README) – every 30 minutes in the background
/// (<see cref="ScheduleFeedWorker"/>), at once when a link is added, or with "Opdater nu". The lessons are kept in memory
/// and on disk (skole/kalender/{id}.json), so the timetable still shows when the school's server is down or after a restart.
/// The link holds a private key: it is never logged, only its host.
/// Every fetch is compared with the one before: changes in the week on the screen (from today) wait in
/// <see cref="ChangesFor"/> until someone taps "Markér som set" (<see cref="AcknowledgeAsync"/>) or the day has passed.
/// </summary>
public sealed partial class ScheduleFeedService
{
    public const string HttpClientName = "FamilyHub.SchoolFeed";

    /// <summary>A calendar changes rarely – twice an hour is plenty.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    /// <summary>After a failure, try again a little sooner.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>The page shows this week (and the widget tomorrow) – keep a little before and a few months ahead.</summary>
    public static readonly TimeSpan KeepBefore = TimeSpan.FromDays(14);
    public static readonly TimeSpan KeepAfter = TimeSpan.FromDays(120);

    /// <summary>A school calendar is a few hundred kilobytes – anything far bigger is not one (set on the HttpClient too).</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <param name="Source">Fingerprint of the address the lessons came from (<see cref="SourceOf"/>) – null in caches from before it existed.</param>
    private sealed record FeedCache(DateTimeOffset FetchedAt, IReadOnlyList<FeedEvent> Events, IReadOnlyList<FeedChange>? Changes = null, string? Source = null);

    private readonly IHttpClientFactory httpClients;
    private readonly SchoolScheduleService schools;
    private readonly IAppDataPaths paths;
    private readonly IHubClock clock;
    private readonly ILogger<ScheduleFeedService> logger;
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<FeedEvent>> events = new();
    private readonly ConcurrentDictionary<Guid, FeedStatus> statuses = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<FeedChange>> changes = new();
    private readonly ConcurrentDictionary<Guid, string> sources = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public ScheduleFeedService(IHttpClientFactory httpClients, SchoolScheduleService schools, IAppDataPaths paths, IHubClock clock, ILogger<ScheduleFeedService> logger)
    {
        this.httpClients = httpClients;
        this.schools = schools;
        this.paths = paths;
        this.clock = clock;
        this.logger = logger;

        // Last known lessons from disk – the timetable has something to show before the first fetch.
        foreach (var schedule in schools.Schedules.Where(s => s.UsesFeed))
        {
            if (Store(schedule.Id).Load() is { } cache)
            {
                events[schedule.Id] = cache.Events;
                statuses[schedule.Id] = new FeedStatus { LastSuccess = cache.FetchedAt };
                changes[schedule.Id] = cache.Changes ?? [];
                if (cache.Source is { } source)
                {
                    sources[schedule.Id] = source;
                }
            }
        }
    }

    /// <summary>Raised after a fetch (success or failure) – from a background thread; components use <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>The lessons in the schedule's calendar, oldest first – empty when not fetched yet.</summary>
    public IReadOnlyList<FeedEvent> EventsFor(Guid scheduleId) => events.GetValueOrDefault(scheduleId) ?? [];

    public FeedStatus StatusOf(Guid scheduleId) => statuses.GetValueOrDefault(scheduleId) ?? FeedStatus.Never;

    /// <summary>Changes in the calendar that nobody has marked as seen yet – today and later, oldest first.</summary>
    public IReadOnlyList<FeedChange> ChangesFor(Guid scheduleId)
    {
        var today = clock.Today;
        return [.. (changes.GetValueOrDefault(scheduleId) ?? []).Where(c => c.Date >= today)];
    }

    /// <summary>"Markér som set": the warning goes away on every screen.</summary>
    public async Task AcknowledgeAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (ChangesFor(scheduleId).Count == 0)
        {
            return;
        }

        changes[scheduleId] = [];
        Changed?.Invoke();
        await SaveCacheAsync(scheduleId, cancellationToken);
    }

    /// <summary>"www.moodle.aau.dk" – what the screen shows instead of the private link.</summary>
    public static string HostOf(string url) => TryNormalize(url, out var uri) ? uri.Host : "kalender-linket";

    /// <summary>
    /// webcal:// becomes https://; only http(s) is fetched. A Moodle export of one week or month is fetched as "Seneste og
    /// næste 60 dage": a fixed period never reaches next week, and AAU's Moodle gives last week for "Denne uge".
    /// </summary>
    public static bool TryNormalize(string? url, out Uri uri)
    {
        uri = null!;
        var text = url?.Trim() ?? "";
        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = parsed.AbsolutePath.EndsWith("/calendar/export_execute.php", StringComparison.OrdinalIgnoreCase)
            ? new Uri(MoodleFixedPeriod().Replace(parsed.AbsoluteUri, "recentupcoming"))
            : parsed;
        return true;
    }

    [GeneratedRegex(@"(?<=[?&]preset_time=)(weeknow|weeknext|monthnow)(?=&|#|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MoodleFixedPeriod();

    /// <summary>
    /// Which address the lessons came from – a fingerprint, so the private link is not written to the cache. When it changes
    /// (a new link, or an old link now fetched differently), the next fetch is a new starting point, not a list of changes.
    /// </summary>
    internal static string? SourceOf(string? url) => TryNormalize(url, out var uri)
        ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..16]
        : null;

    /// <summary>Downloads and reads a link without saving anything – used when a link is added. Throws <see cref="FeedException"/>.</summary>
    public async Task<IReadOnlyList<FeedEvent>> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryNormalize(url, out var uri))
        {
            throw new FeedException(FeedProblem.InvalidLink, "Skriv et link, der starter med https:// eller webcal://.");
        }

        string text;
        try
        {
            using var client = httpClients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("FamilyHub/1.0");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                throw new FeedException(FeedProblem.Rejected, "Linket virker ikke længere. Hent et nyt link fra skolens kalender.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new FeedException(FeedProblem.Offline, "Skolens kalender svarede ikke. Prøv igen om lidt.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                throw new FeedException(FeedProblem.NotCalendar, "Kalenderen er for stor til at blive hentet.");
            }

            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (FeedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new FeedException(FeedProblem.Offline, "Skolens kalender kunne ikke nås. Tjek forbindelsen, og prøv igen.", ex);
        }

        var now = clock.Now;
        try
        {
            var occurrences = IcsParser.Parse(text, now - KeepBefore, now + KeepAfter, clock.TimeZone);
            return [.. occurrences.Select(o => new FeedEvent(
                TimeZoneInfo.ConvertTime(o.Start, clock.TimeZone).DateTime,
                TimeZoneInfo.ConvertTime(o.End, clock.TimeZone).DateTime,
                o.Title,
                o.Location,
                o.Key,
                o.Description))];
        }
        catch (IcsFormatException ex)
        {
            throw new FeedException(FeedProblem.NotCalendar, "Linket giver ikke en kalender. Kopiér iCal-linket fra skolens kalender (fx »Hent kalender-URL« i Moodle).", ex);
        }
    }

    /// <summary>
    /// Keeps lessons fetched when a link is added – in memory and on disk. A new link is the starting point:
    /// nothing in it counts as a change.
    /// </summary>
    public Task SaveAsync(Guid scheduleId, IReadOnlyList<FeedEvent> fetched, CancellationToken cancellationToken = default) =>
        StoreAsync(scheduleId, fetched, [], cancellationToken);

    /// <summary>The week on the screen from today – where a change needs the family's attention.</summary>
    internal (DateTime From, DateTime To) WatchedWindow()
    {
        var today = clock.Today;
        return (today.ToDateTime(TimeOnly.MinValue), ScheduleRules.DisplayedMonday(today).AddDays(7).ToDateTime(TimeOnly.MinValue));
    }

    private async Task StoreAsync(Guid scheduleId, IReadOnlyList<FeedEvent> fetched, IReadOnlyList<FeedChange> pending, CancellationToken cancellationToken)
    {
        var now = clock.Now;
        events[scheduleId] = fetched;
        changes[scheduleId] = pending;
        if (SourceOf(schools.Find(scheduleId)?.FeedUrl) is { } source)
        {
            sources[scheduleId] = source;
        }

        statuses[scheduleId] = new FeedStatus { LastSuccess = now, LastAttempt = now };
        Changed?.Invoke();
        await SaveCacheAsync(scheduleId, cancellationToken);
    }

    private Task SaveCacheAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        Store(scheduleId).SaveAsync(
            new FeedCache(StatusOf(scheduleId).LastSuccess ?? clock.Now, EventsFor(scheduleId), changes.GetValueOrDefault(scheduleId) ?? [], sources.GetValueOrDefault(scheduleId)),
            cancellationToken);

    /// <summary>Fetches one schedule's calendar now ("Opdater nu"). Failures are kept in the status, not thrown.</summary>
    public async Task RefreshAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (schools.Find(scheduleId) is not { UsesFeed: true } schedule)
        {
            return;
        }

        var now = clock.Now;
        try
        {
            var fetched = await FetchAsync(schedule.FeedUrl!, cancellationToken);

            // Compare with the last fetch – unless there is none, or it came from another address (then this is the starting point).
            var pending = changes.GetValueOrDefault(scheduleId) ?? [];
            if (StatusOf(scheduleId).LastSuccess is not null && sources.GetValueOrDefault(scheduleId) == SourceOf(schedule.FeedUrl))
            {
                var (from, to) = WatchedWindow();
                pending = FeedChanges.Merge(pending, FeedChanges.Detect(EventsFor(scheduleId), fetched, from, to, now), clock.Today);
            }
            else
            {
                pending = [];
            }

            await StoreAsync(scheduleId, fetched, pending, cancellationToken);
        }
        catch (FeedException ex)
        {
            logger.Log(ex.Problem == FeedProblem.Offline ? LogLevel.Information : LogLevel.Warning, ex.InnerException,
                "School calendar from {Host} could not be fetched: {Problem}", HostOf(schedule.FeedUrl!), ex.Problem);
            statuses[scheduleId] = StatusOf(scheduleId) with { LastAttempt = now, Problem = ex.Problem };
            Changed?.Invoke();
        }
    }

    /// <summary>Every linked schedule that is due – called by the worker. One at a time; a slow school never blocks the others for long.</summary>
    public async Task RefreshDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await refreshLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var now = clock.Now;
            foreach (var schedule in schools.Schedules.Where(s => s.UsesFeed))
            {
                var status = StatusOf(schedule.Id);
                var wait = status.Problem == FeedProblem.None ? RefreshInterval : RetryInterval;
                if (status.LastAttempt is not { } last || now - last >= wait)
                {
                    await RefreshAsync(schedule.Id, cancellationToken);
                }
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private JsonFileStore<FeedCache> Store(Guid scheduleId) =>
        new(paths.GetFilePath($"skole/kalender/{scheduleId:N}.json"), logger);
}
