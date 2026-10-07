using System.Net;
using System.Text;
using System.Text.Json;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Household;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Calendar.Google;
using FamilyHub.Modules.Calendar.Model;
using FamilyHub.Modules.Calendar.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Calendar;

public sealed class CalendarServiceTests : IDisposable
{
    private static readonly Uri Callback = new("http://localhost:5080/kalender/google/callback");

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)); // 10:00 in Copenhagen
    private readonly EphemeralDataProtectionProvider protection = new();
    private readonly FakeGoogle google = new();
    private readonly List<IDisposable> disposables = [];

    public CalendarServiceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(directory, recursive: true);
    }

    private (CalendarService Service, GoogleOAuthClient OAuth, IHouseholdService Household) Create()
    {
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        var household = new HouseholdService(paths, NullLogger<HouseholdService>.Instance);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FamilyHub:Calendar:Google:ClientId"] = "client-id",
            ["FamilyHub:Calendar:Google:ClientSecret"] = "client-secret",
        }).Build();
        var http = new FakeHttpClientFactory(google);
        var oauth = new GoogleOAuthClient(http, new GoogleCredentialsProvider(configuration, paths, new HostingEnvironment { ContentRootPath = directory }, NullLogger<GoogleCredentialsProvider>.Instance), time);
        var service = new CalendarService(oauth, new GoogleCalendarApi(http), paths, clock, time, household, protection, NullLogger<CalendarService>.Instance);
        disposables.Add(clock);
        disposables.Add(service);
        return (service, oauth, household);
    }

    private static async Task SignInAsync(CalendarService service)
    {
        var url = service.BeginSignIn(Callback);
        var state = QueryHelpers.ParseQuery(url.Query)["state"].ToString();
        await service.CompleteSignInAsync(state, "one-time-code");
    }

    [Fact]
    public void A_configured_key_file_is_read_relative_to_the_content_root()
    {
        var contentRoot = Directory.CreateDirectory(Path.Combine(directory, "src", "web")).FullName;
        Directory.CreateDirectory(Path.Combine(directory, "secrets"));
        File.WriteAllText(Path.Combine(directory, "secrets", "google-client.json"), """{"web":{"client_id":"id-dev","client_secret":"s"}}""");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FamilyHub:Calendar:Google:KeyFile"] = "../../secrets/google-client.json",
        }).Build();
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = Path.Combine(directory, "data") }));

        var provider = new GoogleCredentialsProvider(configuration, paths, new HostingEnvironment { ContentRootPath = contentRoot }, NullLogger<GoogleCredentialsProvider>.Instance);

        Assert.Equal(Path.Combine(directory, "secrets", "google-client.json"), provider.FilePath);
        Assert.Equal("id-dev", provider.Get()?.ClientId);
    }

    [Fact]
    public void Sign_in_asks_for_the_narrow_calendar_scopes_with_pkce()
    {
        var (_, oauth, _) = Create();

        var query = QueryHelpers.ParseQuery(oauth.CreateSignInUrl(Callback).Query);

        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal(Callback.ToString(), query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Contains("https://www.googleapis.com/auth/calendar.events", query["scope"].ToString());
        Assert.Contains("https://www.googleapis.com/auth/calendar.calendarlist.readonly", query["scope"].ToString());
        Assert.DoesNotContain("auth/calendar ", query["scope"].ToString() + " ");
    }

    [Theory]
    [InlineData("http://localhost:5000/", true)]
    [InlineData("http://127.0.0.1:5080/", true)]
    [InlineData("https://hub.example.com/", true)]
    [InlineData("http://familyhub.local:5000/", false)]
    [InlineData("http://10.64.70.46:8080/", false)]
    public void Sign_in_only_works_on_https_or_a_loopback_address(string baseUri, bool allowed)
    {
        Assert.Equal(allowed, GoogleOAuthClient.CanSignInFrom(new Uri(baseUri)));
    }

    [Fact]
    public async Task Completing_sign_in_proves_the_code_verifier_and_stores_the_account_encrypted()
    {
        var (service, oauth, _) = Create();
        var url = service.BeginSignIn(Callback);
        var query = QueryHelpers.ParseQuery(url.Query);

        var account = await service.CompleteSignInAsync(query["state"]!, "one-time-code");

        var tokenRequest = google.Form(google.Requests.First(r => r.Url == GoogleOAuthClient.TokenEndpoint));
        Assert.Equal("authorization_code", tokenRequest["grant_type"]);
        Assert.Equal("one-time-code", tokenRequest["code"]);
        Assert.Equal(query["code_challenge"].ToString(), GoogleOAuthClient.CodeChallenge(tokenRequest["code_verifier"]));
        Assert.Equal("heine@example.com", account.Email);
        Assert.Equal("google-user-1", account.Id);

        var file = await File.ReadAllTextAsync(Path.Combine(directory, "kalender", "google-konti.json"));
        Assert.DoesNotContain(FakeGoogle.RefreshToken, file);
    }

    [Fact]
    public async Task An_unknown_or_reused_state_is_refused()
    {
        var (service, _, _) = Create();
        var state = QueryHelpers.ParseQuery(service.BeginSignIn(Callback).Query)["state"].ToString();
        await service.CompleteSignInAsync(state, "code");

        await Assert.ThrowsAsync<GoogleAuthException>(() => service.CompleteSignInAsync(state, "code"));
        await Assert.ThrowsAsync<GoogleAuthException>(() => service.CompleteSignInAsync("made-up", "code"));
    }

    [Fact]
    public async Task Sync_finds_calendars_links_family_members_and_fetches_visible_events()
    {
        var (service, _, household) = Create();
        await household.UpdateAsync(h => h with { Members = [new FamilyMember { Name = "Emma", Color = MemberColor.Sand }] });
        await SignInAsync(service);

        await service.SyncAsync();

        Assert.Equal(SyncProblem.None, service.Status.Problem);
        Assert.True(service.HasData);
        var emma = Assert.Single(service.Calendars, c => c.Name == "Emma");
        Assert.Equal("Emma", emma.Member?.Name);
        Assert.Equal(MemberColor.Sand, emma.Color);
        Assert.False(service.Calendars.Single(c => c.Name == "Gamle ting").Visible); // hidden in Google → hidden here

        var events = service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28));
        Assert.Equal(["Tandlæge", "Fodbold"], events.Select(e => e.Title));
        Assert.DoesNotContain(google.Requests, r => r.Url.Contains("old%40group", StringComparison.Ordinal) && r.Url.Contains("/events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_local_copy_survives_a_restart()
    {
        var (first, _, _) = Create();
        await SignInAsync(first);
        await first.SyncAsync();

        var (second, _, _) = Create();

        Assert.True(second.HasData);
        Assert.Equal(2, second.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)).Count);
        Assert.Single(second.Accounts);
    }

    [Fact]
    public async Task Offline_keeps_showing_the_last_events()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();
        var lastSuccess = service.Status.LastSuccess;

        google.Offline = true;
        time.Advance(TimeSpan.FromMinutes(5));
        await service.SyncAsync();

        Assert.Equal(SyncProblem.Offline, service.Status.Problem);
        Assert.Equal(lastSuccess, service.Status.LastSuccess);
        Assert.Equal(2, service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)).Count);
    }

    [Fact]
    public async Task A_revoked_account_must_be_connected_again_but_its_events_stay()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();

        google.RefreshError = "invalid_grant";
        time.Advance(TimeSpan.FromHours(2)); // access token expired → refresh → refused
        await service.SyncAsync();

        Assert.Equal(SyncProblem.NeedsReconnect, service.Status.Problem);
        Assert.True(Assert.Single(service.Accounts).NeedsReconnect);
        Assert.Equal(2, service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)).Count);
    }

    [Fact]
    public async Task Added_events_are_sent_to_google_and_shown_right_away()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();

        var created = await service.AddEventAsync(new CalendarEventDraft
        {
            CalendarId = "emma@group",
            Title = "Klaver",
            Date = new DateOnly(2026, 9, 30),
            StartTime = new TimeOnly(15, 0),
            Duration = TimeSpan.FromMinutes(45),
        });

        var post = google.Requests.Single(r => r.Method == HttpMethod.Post && r.Url.Contains("/events", StringComparison.Ordinal));
        Assert.Contains("\"summary\":\"Klaver\"", post.Body);
        Assert.Contains(service.GetEvents(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1)), e => e.Key == created.Key);
    }

    [Fact]
    public async Task Edited_events_are_patched_in_google_and_shown_right_away()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();
        var dentist = service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 22)).Single();
        Assert.True(service.CanChange(dentist));

        var moved = await service.UpdateEventAsync(dentist, new CalendarEventDraft
        {
            CalendarId = dentist.CalendarId,
            Title = "Tandlæge",
            Date = new DateOnly(2026, 9, 22),
            StartTime = new TimeOnly(13, 0),
            Duration = TimeSpan.FromHours(1),
        });

        var patch = google.Requests.Single(r => r.Method == HttpMethod.Patch);
        Assert.Contains("/events/t1?", patch.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("summary", patch.Body, StringComparison.Ordinal); // only the time changed
        Assert.Empty(service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 22)));
        Assert.Equal(moved.Key, service.GetEvents(new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 23)).Single().Key);
    }

    [Fact]
    public async Task Deleted_events_disappear_at_once_and_undo_brings_them_back()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();
        var week = (From: new DateOnly(2026, 9, 21), To: new DateOnly(2026, 9, 28));
        var dentist = service.GetEvents(week.From, week.To).Single(e => e.Title == "Tandlæge");

        await service.DeleteEventAsync(dentist);

        Assert.Contains(google.Requests, r => r.Method == HttpMethod.Delete && r.Url.Contains("/events/t1", StringComparison.Ordinal));
        Assert.Equal(["Fodbold"], service.GetEvents(week.From, week.To).Select(e => e.Title));

        await service.RestoreEventAsync(dentist);

        Assert.Contains("\"status\":\"confirmed\"", google.Requests.Last(r => r.Method == HttpMethod.Patch).Body, StringComparison.Ordinal);
        Assert.Equal(["Tandlæge", "Fodbold"], service.GetEvents(week.From, week.To).Select(e => e.Title));
    }

    [Fact]
    public async Task Invitations_and_read_only_calendars_cannot_be_changed_from_the_screen()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();
        var dentist = service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 22)).Single();

        Assert.False(service.CanChange(dentist with { Restriction = EventRestriction.Invitation }));
        Assert.False(service.CanChange(dentist with { CalendarId = "old@group" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteEventAsync(dentist with { CalendarId = "old@group" }));
    }

    [Fact]
    public async Task Removing_an_account_hides_its_calendars_and_undo_brings_it_back()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();
        var account = service.Accounts.Single();

        await service.RemoveAccountAsync(account.Id);

        Assert.Empty(service.Accounts);
        Assert.Empty(service.Calendars);
        Assert.Empty(service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)));

        await service.RestoreAccountAsync(account);
        await service.SyncAsync();

        Assert.Equal(2, service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)).Count);
    }

    [Fact]
    public async Task Hiding_a_calendar_removes_its_events_from_the_screens()
    {
        var (service, _, _) = Create();
        await SignInAsync(service);
        await service.SyncAsync();

        await service.UpdatePreferenceAsync("emma@group", p => p with { Visible = false });

        Assert.Equal(["Tandlæge"], service.GetEvents(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)).Select(e => e.Title));
    }

    [Fact]
    public void The_same_calendar_from_two_accounts_is_kept_once_preferring_write_access()
    {
        var accounts = new[]
        {
            new GoogleAccount { Id = "heine", Email = "h", ProtectedRefreshToken = "x" },
            new GoogleAccount { Id = "charlotte", Email = "c", ProtectedRefreshToken = "x" },
        };
        var sources = new[]
        {
            new CalendarSource { Id = "kids", AccountId = "heine", Name = "Børn", CanWrite = false },
            new CalendarSource { Id = "kids", AccountId = "charlotte", Name = "Børn", CanWrite = true },
            new CalendarSource { Id = "heine@example.com", AccountId = "heine", Name = "Heine", CanWrite = true },
        };

        var merged = CalendarService.MergeSources(sources, accounts);

        Assert.Equal(2, merged.Count);
        Assert.Equal("charlotte", merged.Single(s => s.Id == "kids").AccountId);
    }

    /// <summary>Just enough of Google's token endpoint and Calendar API.</summary>
    private sealed class FakeGoogle : HttpMessageHandler
    {
        public const string RefreshToken = "refresh-token-secret";

        public List<(HttpMethod Method, string Url, string Body)> Requests { get; } = [];

        public bool Offline { get; set; }

        public string? RefreshError { get; set; }

        public Dictionary<string, string> Form((HttpMethod Method, string Url, string Body) request) =>
            QueryHelpers.ParseQuery(request.Body).ToDictionary(p => p.Key, p => p.Value.ToString());

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            Requests.Add((request.Method, request.RequestUri.ToString().StartsWith(GoogleOAuthClient.TokenEndpoint, StringComparison.Ordinal) ? GoogleOAuthClient.TokenEndpoint : request.RequestUri.ToString(), body));

            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            if (url == GoogleOAuthClient.TokenEndpoint)
            {
                var form = QueryHelpers.ParseQuery(body);
                if (form["grant_type"] == "refresh_token" && RefreshError is { } error)
                {
                    return Json(new { error }, HttpStatusCode.BadRequest);
                }

                return Json(new
                {
                    access_token = $"access-{Guid.NewGuid():N}",
                    expires_in = 3599,
                    refresh_token = form["grant_type"] == "authorization_code" ? RefreshToken : null,
                    id_token = IdToken(),
                });
            }

            if (url.EndsWith("/users/me/calendarList", StringComparison.Ordinal))
            {
                return Json(new
                {
                    items = new object[]
                    {
                        new { id = "heine@example.com", summary = "heine@example.com", accessRole = "owner", primary = true, selected = true },
                        new { id = "emma@group", summary = "Emma", accessRole = "writer", selected = true },
                        new { id = "old@group", summary = "Gamle ting", accessRole = "reader", selected = false },
                    },
                });
            }

            if (url.EndsWith("/events", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                using var sent = JsonDocument.Parse(body);
                return Json(new
                {
                    id = "created-1",
                    summary = sent.RootElement.GetProperty("summary").GetString(),
                    start = new { dateTime = sent.RootElement.GetProperty("start").GetProperty("dateTime").GetString() },
                    end = new { dateTime = sent.RootElement.GetProperty("end").GetProperty("dateTime").GetString() },
                });
            }

            if (url.Contains("/calendars/heine%40example.com/events/t1", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Delete)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                // PATCH: the stored event with the sent changes on top.
                using var sent = JsonDocument.Parse(body);
                var root = sent.RootElement;
                string Text(string name, string fallback) => root.TryGetProperty(name, out var value) ? value.GetString()! : fallback;
                string Time(string name, string fallback) => root.TryGetProperty(name, out var value) ? value.GetProperty("dateTime").GetString()! : fallback;
                return Json(Timed("t1", Text("summary", "Tandlæge"), Time("start", "2026-09-21T09:00:00+02:00"), Time("end", "2026-09-21T10:00:00+02:00")));
            }

            if (url.Contains("/calendars/heine%40example.com/events", StringComparison.Ordinal))
            {
                return Json(new { items = new[] { Timed("t1", "Tandlæge", "2026-09-21T09:00:00+02:00", "2026-09-21T10:00:00+02:00") } });
            }

            if (url.Contains("/calendars/emma%40group/events", StringComparison.Ordinal))
            {
                return Json(new { items = new[] { Timed("f1", "Fodbold", "2026-09-24T16:00:00+02:00", "2026-09-24T17:30:00+02:00") } });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static object Timed(string id, string summary, string start, string end) =>
            new { id, summary, start = new { dateTime = start }, end = new { dateTime = end } };

        private static string IdToken()
        {
            static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return $"{Encode("""{"alg":"none"}""")}.{Encode("""{"sub":"google-user-1","email":"heine@example.com"}""")}.sig";
        }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
