using System.Collections.Concurrent;
using System.Security.Cryptography;
using FamilyHub.Core.Household;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Calendar.Google;
using FamilyHub.Modules.Calendar.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.Calendar.Services;

/// <summary>
/// The family calendar, shared by every screen (singleton). Keeps the connected Google accounts,
/// the calendars and a local copy of the events around today, so the kitchen screen shows the
/// calendar instantly – also after a restart without internet. <see cref="CalendarSyncWorker"/>
/// refreshes it in the background; <see cref="Changed"/> tells the screens.
/// </summary>
public sealed class CalendarService : IDisposable
{
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);

    /// <summary>Months before and after the current month that are kept locally.</summary>
    internal const int MonthsBack = 2;
    internal const int MonthsAhead = 13;

    private const int ParallelFetches = 4;

    private readonly GoogleOAuthClient oauth;
    private readonly GoogleCalendarApi api;
    private readonly IHubClock clock;
    private readonly TimeProvider time;
    private readonly IHouseholdService household;
    private readonly IDataProtector protector;
    private readonly ILogger<CalendarService> logger;

    private readonly JsonFileStore<AccountsFile> accountsStore;
    private readonly JsonFileStore<PreferencesFile> preferencesStore;
    private readonly JsonFileStore<CalendarSnapshot> snapshotStore;

    private readonly SemaphoreSlim syncGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly SemaphoreSlim syncRequested = new(0, 1);
    private readonly ConcurrentDictionary<string, GoogleAccessToken> accessTokens = new();
    private readonly ConcurrentDictionary<DateOnly, IReadOnlyList<CalendarEvent>> extraMonths = new();

    private volatile IReadOnlyList<GoogleAccount> accounts;
    private volatile IReadOnlyList<CalendarPreference> preferences;
    private volatile CalendarSnapshot snapshot;
    private volatile CalendarSyncStatus status;
    private int lastSavedFingerprint;

    public CalendarService(
        GoogleOAuthClient oauth,
        GoogleCalendarApi api,
        IAppDataPaths paths,
        IHubClock clock,
        TimeProvider time,
        IHouseholdService household,
        IDataProtectionProvider dataProtection,
        ILogger<CalendarService> logger)
    {
        this.oauth = oauth;
        this.api = api;
        this.clock = clock;
        this.time = time;
        this.household = household;
        this.logger = logger;
        protector = dataProtection.CreateProtector("FamilyHub.Calendar.GoogleRefreshToken.v1");

        accountsStore = new JsonFileStore<AccountsFile>(paths.GetFilePath("kalender/google-konti.json"), logger);
        preferencesStore = new JsonFileStore<PreferencesFile>(paths.GetFilePath("kalender/kalendere.json"), logger);
        snapshotStore = new JsonFileStore<CalendarSnapshot>(paths.GetFilePath("kalender/cache.json"), logger);

        accounts = accountsStore.Load()?.Accounts ?? [];
        preferences = preferencesStore.Load()?.Calendars ?? [];
        snapshot = snapshotStore.Load() ?? CalendarSnapshot.Empty;
        lastSavedFingerprint = snapshot.Fingerprint();
        status = CalendarSyncStatus.Never with { LastSuccess = snapshot.LastSuccess };

        household.Changed += OnHouseholdChanged;
    }

    /// <summary>Raised after anything changed (events, calendars, accounts, status). May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>The Google OAuth client (key file) is in place.</summary>
    public bool IsConfigured => oauth.IsConfigured;

    public IReadOnlyList<GoogleAccount> Accounts => accounts;

    public CalendarSyncStatus Status => status;

    /// <summary>Events have been fetched at least once (now or before a restart).</summary>
    public bool HasData => snapshot.LastSuccess is not null;

    /// <summary>All calendars from the connected accounts: family members' first (in household order), then shared ones.</summary>
    public IReadOnlyList<CalendarEntry> Calendars
    {
        get
        {
            var members = household.Current.Members;
            var byCalendar = preferences.ToDictionary(p => p.CalendarId);
            return
            [
                .. snapshot.Sources
                    .Select(source =>
                    {
                        var preference = byCalendar.GetValueOrDefault(source.Id) ?? new CalendarPreference { CalendarId = source.Id };
                        var member = preference.MemberId is { } id ? members.FirstOrDefault(m => m.Id == id) : null;
                        return new CalendarEntry(source, preference, member);
                    })
                    .OrderBy(c => c.Member is { } m ? members.ToList().IndexOf(m) : int.MaxValue)
                    .ThenBy(c => c.Name, StringComparer.Create(DanishFormat.Culture, ignoreCase: true)),
            ];
        }
    }

    public IReadOnlyList<CalendarEntry> VisibleCalendars => [.. Calendars.Where(c => c.Visible)];

    /// <summary>Events from visible calendars touching [<paramref name="from"/>, <paramref name="toExclusive"/>), in start order.</summary>
    public IReadOnlyList<CalendarEvent> GetEvents(DateOnly from, DateOnly toExclusive)
    {
        var visible = VisibleCalendars.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var current = snapshot;
        return
        [
            .. current.Events
                .Concat(extraMonths.Values.SelectMany(e => e))
                .Where(e => visible.Contains(e.CalendarId) && e.StartDate < toExclusive && e.EndDate > from)
                .DistinctBy(e => e.Key)
                .OrderBy(e => e.Start)
                .ThenByDescending(e => e.IsAllDay)
                .ThenBy(e => e.Title, StringComparer.Create(DanishFormat.Culture, ignoreCase: true)),
        ];
    }

    /// <summary>
    /// Makes sure events for a period outside the local window (far back or far ahead) are loaded.
    /// Returns false if they could not be fetched.
    /// </summary>
    public async Task<bool> EnsureLoadedAsync(DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken = default)
    {
        var current = snapshot;
        var missing = new List<DateOnly>();
        for (var month = MonthStart(from); month < toExclusive; month = month.AddMonths(1))
        {
            var inWindow = month >= current.WindowStart && month < current.WindowEnd;
            if (!inWindow && !extraMonths.ContainsKey(month))
            {
                missing.Add(month);
            }
        }

        if (missing.Count == 0 || accounts.Count == 0)
        {
            return true;
        }

        try
        {
            foreach (var month in missing)
            {
                extraMonths[month] = await FetchEventsAsync(VisibleSources(), month, month.AddMonths(1), cancellationToken);
            }

            NotifyChanged();
            return true;
        }
        catch (Exception ex) when (Classify(ex, cancellationToken) is not null)
        {
            logger.LogWarning(ex, "Could not load calendar events for {From}–{To}", from, toExclusive);
            return false;
        }
    }

    // ------------------------------------------------------------------ accounts

    /// <summary>Starts connecting a Google account. Returns Google's sign-in page.</summary>
    public Uri BeginSignIn(Uri redirectUri) => oauth.CreateSignInUrl(redirectUri);

    /// <summary>Finishes the sign-in, stores the account and starts fetching its calendars.</summary>
    public async Task<GoogleAccount> CompleteSignInAsync(string state, string code, CancellationToken cancellationToken = default)
    {
        var signIn = await oauth.CompleteSignInAsync(state, code, cancellationToken);
        var account = new GoogleAccount
        {
            Id = signIn.AccountId,
            Email = signIn.Email,
            ProtectedRefreshToken = protector.Protect(signIn.RefreshToken),
            ConnectedAt = clock.Now,
        };

        await SaveAccountsAsync(list => [.. list.Where(a => a.Id != account.Id), account], cancellationToken);
        accessTokens[account.Id] = signIn.AccessToken;
        RequestSync();
        return account;
    }

    /// <summary>Removes the account and its calendars from this household (right away – offer "Fortryd").</summary>
    public async Task RemoveAccountAsync(string accountId)
    {
        await SaveAccountsAsync(list => [.. list.Where(a => a.Id != accountId)]);
        accessTokens.TryRemove(accountId, out _);
        extraMonths.Clear();

        var current = snapshot;
        var removed = current.Sources.Where(s => s.AccountId == accountId).Select(s => s.Id).ToHashSet();
        snapshot = current with
        {
            Sources = [.. current.Sources.Where(s => s.AccountId != accountId)],
            Events = [.. current.Events.Where(e => !removed.Contains(e.CalendarId))],
        };
        await SaveSnapshotAsync();
        NotifyChanged();
        RequestSync();
    }

    /// <summary>Undo for <see cref="RemoveAccountAsync"/>.</summary>
    public async Task RestoreAccountAsync(GoogleAccount account)
    {
        await SaveAccountsAsync(list => [.. list.Where(a => a.Id != account.Id), account]);
        RequestSync();
    }

    // ------------------------------------------------------------------ calendars and events

    public async Task UpdatePreferenceAsync(string calendarId, Func<CalendarPreference, CalendarPreference> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var becameVisible = false;
        await writeGate.WaitAsync();
        try
        {
            var existing = preferences.FirstOrDefault(p => p.CalendarId == calendarId) ?? new CalendarPreference { CalendarId = calendarId };
            var updated = update(existing) with { CalendarId = calendarId };
            becameVisible = updated.Visible && !existing.Visible;
            var list = preferences.Where(p => p.CalendarId != calendarId).Append(updated).ToList();
            await preferencesStore.SaveAsync(new PreferencesFile { Calendars = list });
            preferences = list;
        }
        finally
        {
            writeGate.Release();
        }

        NotifyChanged();

        // Events are only fetched for visible calendars.
        if (becameVisible)
        {
            RequestSync();
        }
    }

    /// <summary>Adds an appointment in Google and shows it right away.</summary>
    public async Task<CalendarEvent> AddEventAsync(NewCalendarEvent newEvent, CancellationToken cancellationToken = default)
    {
        var source = snapshot.Sources.FirstOrDefault(s => s.Id == newEvent.CalendarId && s.CanWrite)
            ?? throw new InvalidOperationException("Kalenderen kan ikke få nye aftaler.");
        var account = accounts.FirstOrDefault(a => a.Id == source.AccountId)
            ?? throw new InvalidOperationException("Google-kontoen er ikke forbundet.");

        var created = await WithAccessTokenAsync(account, token => api.InsertEventAsync(token, newEvent, clock.TimeZone, cancellationToken), cancellationToken);

        var current = snapshot;
        if (created.StartDate < current.WindowEnd && created.EndDate > current.WindowStart)
        {
            snapshot = current with { Events = [.. current.Events.Where(e => e.Key != created.Key), created] };
        }
        else
        {
            var month = MonthStart(created.StartDate);
            if (extraMonths.TryGetValue(month, out var list))
            {
                extraMonths[month] = [.. list, created];
            }
        }

        NotifyChanged();

        // A sync that was already running may not have seen the new event – one more round makes sure it stays.
        RequestSync();
        return created;
    }

    // ------------------------------------------------------------------ synchronisation

    /// <summary>Asks the background worker to synchronise as soon as possible.</summary>
    public void RequestSync()
    {
        try
        {
            if (syncRequested.CurrentCount == 0)
            {
                syncRequested.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Already requested.
        }
    }

    /// <summary>Waits until the next regular sync is due or one is requested.</summary>
    internal async Task WaitForNextSyncAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(SyncInterval, time, linked.Token);
        var requested = syncRequested.WaitAsync(linked.Token);
        await Task.WhenAny(delay, requested);
        await linked.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Fetches calendars and events from every connected account. Safe to call at any time.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (accounts.Count == 0 || !oauth.IsConfigured)
        {
            return;
        }

        await syncGate.WaitAsync(cancellationToken);
        try
        {
            SetStatus(status with { IsSyncing = true });
            var problem = await SyncCoreAsync(cancellationToken);
            var now = clock.Now;
            var succeeded = problem is SyncProblem.None or SyncProblem.NeedsReconnect;
            if (succeeded)
            {
                snapshot = snapshot with { LastSuccess = now };
            }

            await SaveSnapshotAsync();

            SetStatus(new CalendarSyncStatus
            {
                LastAttempt = now,
                LastSuccess = succeeded ? now : status.LastSuccess,
                Problem = problem,
            });
        }
        finally
        {
            if (status.IsSyncing)
            {
                SetStatus(status with { IsSyncing = false });
            }

            syncGate.Release();
        }
    }

    private async Task<SyncProblem> SyncCoreAsync(CancellationToken cancellationToken)
    {
        var problem = SyncProblem.None;
        var previous = snapshot;
        var sources = new List<CalendarSource>();
        var failedAccounts = new HashSet<string>();

        // 1. Which calendars does each account see?
        foreach (var account in accounts)
        {
            try
            {
                if (account.NeedsReconnect)
                {
                    throw new GoogleAuthException("Kontoen skal forbindes igen.");
                }

                sources.AddRange(await WithAccessTokenAsync(account, token => api.ListCalendarsAsync(token, account.Id, cancellationToken), cancellationToken));
            }
            catch (Exception ex) when (Classify(ex, cancellationToken) is { } kind)
            {
                problem = Worst(problem, kind);
                failedAccounts.Add(account.Id);
                sources.AddRange(previous.Sources.Where(s => s.AccountId == account.Id));
                if (kind == SyncProblem.NeedsReconnect)
                {
                    await MarkNeedsReconnectAsync(account.Id);
                }
                else
                {
                    logger.LogWarning(ex, "Could not list calendars for {Account}", account.Email);
                }
            }
        }

        var merged = MergeSources(sources, accounts);
        await EnsurePreferencesAsync(merged);

        // 2. Events for the visible calendars.
        var visibleIds = preferences.Where(p => p.Visible).Select(p => p.CalendarId).ToHashSet();
        var today = clock.Today;
        var windowStart = MonthStart(today).AddMonths(-MonthsBack);
        var windowEnd = MonthStart(today).AddMonths(MonthsAhead);
        var events = new ConcurrentBag<CalendarEvent>();
        var fetchProblem = SyncProblem.None;
        var fetchGate = new object();

        await Parallel.ForEachAsync(
            merged.Where(s => visibleIds.Contains(s.Id)),
            new ParallelOptions { MaxDegreeOfParallelism = ParallelFetches, CancellationToken = cancellationToken },
            async (source, token) =>
            {
                IEnumerable<CalendarEvent> result;
                if (failedAccounts.Contains(source.AccountId))
                {
                    result = previous.Events.Where(e => e.CalendarId == source.Id);
                }
                else
                {
                    try
                    {
                        result = await FetchEventsAsync([source], windowStart, windowEnd, token);
                    }
                    catch (Exception ex) when (Classify(ex, token) is { } kind)
                    {
                        logger.LogWarning(ex, "Could not fetch events for calendar {Calendar}", source.Name);
                        lock (fetchGate)
                        {
                            fetchProblem = Worst(fetchProblem, kind);
                        }

                        result = previous.Events.Where(e => e.CalendarId == source.Id);
                    }
                }

                foreach (var e in result)
                {
                    events.Add(e);
                }
            });

        snapshot = previous with
        {
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Sources = merged,
            Events = [.. events.DistinctBy(e => e.Key).OrderBy(e => e.Start)],
        };
        extraMonths.Clear();
        NotifyChanged();

        return Worst(problem, fetchProblem);
    }

    private IReadOnlyList<CalendarSource> VisibleSources()
    {
        var visible = preferences.Where(p => p.Visible).Select(p => p.CalendarId).ToHashSet();
        return [.. snapshot.Sources.Where(s => visible.Contains(s.Id))];
    }

    private async Task<IReadOnlyList<CalendarEvent>> FetchEventsAsync(IEnumerable<CalendarSource> sources, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        var start = GoogleMapping.LocalMidnight(from, clock.TimeZone);
        var end = GoogleMapping.LocalMidnight(toExclusive, clock.TimeZone);
        var result = new List<CalendarEvent>();
        foreach (var source in sources)
        {
            var account = accounts.FirstOrDefault(a => a.Id == source.AccountId && !a.NeedsReconnect);
            if (account is null)
            {
                continue;
            }

            result.AddRange(await WithAccessTokenAsync(account, token => api.ListEventsAsync(token, source.Id, start, end, clock.TimeZone, cancellationToken), cancellationToken));
        }

        return result;
    }

    /// <summary>The same calendar can be visible from both parents' accounts – keep one, preferably one that may write.</summary>
    internal static IReadOnlyList<CalendarSource> MergeSources(IEnumerable<CalendarSource> sources, IReadOnlyList<GoogleAccount> accounts)
    {
        var accountOrder = accounts.Select(a => a.Id).ToList();
        return
        [
            .. sources
                .GroupBy(s => s.Id, StringComparer.Ordinal)
                .Select(g => g
                    .OrderByDescending(s => s.CanWrite)
                    .ThenByDescending(s => s.IsPrimary)
                    .ThenBy(s => accountOrder.IndexOf(s.AccountId))
                    .First()),
        ];
    }

    private async Task EnsurePreferencesAsync(IReadOnlyList<CalendarSource> sources)
    {
        var known = preferences.Select(p => p.CalendarId).ToHashSet();
        var found = sources.Where(s => !known.Contains(s.Id)).ToList();
        if (found.Count == 0)
        {
            return;
        }

        await writeGate.WaitAsync();
        try
        {
            var members = household.Current.Members;
            var list = preferences.ToList();
            foreach (var source in found)
            {
                var member = CalendarMatching.GuessMember(source, members);
                list.Add(new CalendarPreference
                {
                    CalendarId = source.Id,
                    Visible = source.SelectedInGoogle,
                    MemberId = member?.Id,
                    Color = member is null ? CalendarMatching.NextSharedColor(members, list) : MemberColor.Stone,
                });
            }

            await preferencesStore.SaveAsync(new PreferencesFile { Calendars = list });
            preferences = list;
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task<T> WithAccessTokenAsync<T>(GoogleAccount account, Func<string, Task<T>> call, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(account, forceRefresh: false, cancellationToken);
        try
        {
            return await call(token);
        }
        catch (GoogleUnauthorizedException)
        {
            // The access token was rejected early – get a new one and try once more.
            token = await GetAccessTokenAsync(account, forceRefresh: true, cancellationToken);
            return await call(token);
        }
    }

    private async Task<string> GetAccessTokenAsync(GoogleAccount account, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && accessTokens.TryGetValue(account.Id, out var cached) && cached.ExpiresAt > time.GetUtcNow().AddMinutes(1))
        {
            return cached.Value;
        }

        string refreshToken;
        try
        {
            refreshToken = protector.Unprotect(account.ProtectedRefreshToken);
        }
        catch (CryptographicException ex)
        {
            // The data protection keys are gone (e.g. data folder restored without "keys").
            throw new GoogleAuthException("Den gemte adgang kan ikke læses.", ex);
        }

        var fresh = await oauth.RefreshAsync(refreshToken, cancellationToken);
        accessTokens[account.Id] = fresh;
        return fresh.Value;
    }

    private async Task MarkNeedsReconnectAsync(string accountId)
    {
        if (accounts.FirstOrDefault(a => a.Id == accountId) is { NeedsReconnect: false } account)
        {
            logger.LogWarning("Google account {Account} must be connected again", account.Email);
            accessTokens.TryRemove(accountId, out _);
            await SaveAccountsAsync(list => [.. list.Select(a => a.Id == accountId ? a with { NeedsReconnect = true } : a)]);
        }
    }

    private async Task SaveAccountsAsync(Func<IReadOnlyList<GoogleAccount>, IReadOnlyList<GoogleAccount>> update, CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            var list = update(accounts);
            await accountsStore.SaveAsync(new AccountsFile { Accounts = [.. list] }, cancellationToken);
            RestrictToOwner(accountsStore.FilePath);
            accounts = list;
        }
        finally
        {
            writeGate.Release();
        }

        NotifyChanged();
    }

    // The tokens are encrypted, but there's no reason for other users on the Pi to read the file at all.
    private void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not restrict permissions on {Path}", path);
        }
    }

    /// <summary>Writes the local copy – only when something actually changed, to spare the SD card.</summary>
    private async Task SaveSnapshotAsync()
    {
        var current = snapshot;
        var fingerprint = current.Fingerprint();
        if (fingerprint == lastSavedFingerprint)
        {
            return;
        }

        try
        {
            await snapshotStore.SaveAsync(current);
            lastSavedFingerprint = fingerprint;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not save the calendar cache");
        }
    }

    private static SyncProblem? Classify(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        GoogleAuthException => SyncProblem.NeedsReconnect,
        GoogleUnauthorizedException => SyncProblem.Failed,
        HttpRequestException { StatusCode: null } => SyncProblem.Offline,
        TaskCanceledException => SyncProblem.Offline, // HttpClient timeout
        HttpRequestException => SyncProblem.Failed,
        System.Text.Json.JsonException => SyncProblem.Failed,
        _ => null,
    };

    private static SyncProblem Worst(SyncProblem a, SyncProblem b) => (SyncProblem)Math.Max((int)a, (int)b);

    private static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    private void SetStatus(CalendarSyncStatus value)
    {
        status = value;
        NotifyChanged();
    }

    private void OnHouseholdChanged(HouseholdSettings settings) => NotifyChanged();

    private void NotifyChanged()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Calendar change subscriber failed");
            }
        }
    }

    public void Dispose()
    {
        household.Changed -= OnHouseholdChanged;
        syncGate.Dispose();
        writeGate.Dispose();
        syncRequested.Dispose();
    }

    internal sealed record AccountsFile
    {
        public List<GoogleAccount> Accounts { get; init; } = [];
    }

    internal sealed record PreferencesFile
    {
        public List<CalendarPreference> Calendars { get; init; } = [];
    }

    /// <summary>The local copy of calendars and events. Stored in <c>kalender/cache.json</c>.</summary>
    internal sealed record CalendarSnapshot
    {
        public static CalendarSnapshot Empty { get; } = new();

        public DateOnly WindowStart { get; init; }

        public DateOnly WindowEnd { get; init; }

        public DateTimeOffset? LastSuccess { get; init; }

        public IReadOnlyList<CalendarSource> Sources { get; init; } = [];

        public IReadOnlyList<CalendarEvent> Events { get; init; } = [];

        public int Fingerprint()
        {
            // LastSuccess itself changes every sync; only whether there has been one counts.
            var hash = new HashCode();
            hash.Add(LastSuccess is null);
            hash.Add(WindowStart);
            hash.Add(WindowEnd);
            foreach (var source in Sources)
            {
                hash.Add(source);
            }

            foreach (var e in Events)
            {
                hash.Add(e);
            }

            return hash.ToHashCode();
        }
    }
}
