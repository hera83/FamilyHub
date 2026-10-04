using System.Net;
using System.Text;
using FamilyHub.Core.Dexcom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.Core;

public sealed class DexcomServiceTests
{
    private const string Key = "ak_test";

    private readonly FakeGlucoseApi api = new();

    private DexcomService Create(string? apiKey = Key, string baseUrl = "https://dexcom.test") =>
        new(new FakeHttpClientFactory(api), new FixedOptions(new DexcomOptions { BaseUrl = baseUrl, ApiKey = apiKey }), NullLogger<DexcomService>.Instance);

    [Fact]
    public async Task Latest_sends_the_key_and_keeps_the_time_offset_the_api_sent()
    {
        api.Answer("/Dexcom/Latest", """{"time":"2026-10-03T21:20:55.773+02:00","valueMmolL":8.6,"trend":"Flat"}""");

        var reading = await Create().GetLatestAsync();

        var request = Assert.Single(api.Requests);
        Assert.Equal("GET /Dexcom/Latest", request.Line);
        Assert.Equal(Key, request.ApiKey);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 21, 20, 55, 773, TimeSpan.FromHours(2)), reading.Time);
        Assert.Equal(8.6, reading.MmolL);
        Assert.Equal(GlucoseTrend.Flat, reading.Trend);
    }

    [Fact]
    public async Task Readings_ask_for_the_period_in_utc_and_come_back_oldest_first()
    {
        api.Answer("/Dexcom/Readings?From=2026-10-03T08%3A00%3A00Z&To=2026-10-03T09%3A00%3A00Z", """
            {"from":"2026-10-03T10:00:00+02:00","to":"2026-10-03T11:00:00+02:00","count":3,"items":[
              {"time":"2026-10-03T10:05:56.024+02:00","valueMmolL":"7.9","trend":"FortyFiveDown"},
              {"time":"2026-10-03T10:00:55.036+02:00","valueMmolL":8.2,"trend":"SingleDown"},
              {"time":null,"valueMmolL":7.5,"trend":"Flat"}]}
            """);
        var copenhagen = TimeSpan.FromHours(2);

        var readings = await Create().GetReadingsAsync(
            new DateTimeOffset(2026, 10, 3, 10, 0, 0, copenhagen), new DateTimeOffset(2026, 10, 3, 11, 0, 0, copenhagen));

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 0, 0, copenhagen), readings.From);
        Assert.Equal([8.2, 7.9], readings.Items.Select(r => r.MmolL));
        Assert.Equal([GlucoseTrend.SingleDown, GlucoseTrend.FortyFiveDown], readings.Items.Select(r => r.Trend));
    }

    [Fact]
    public async Task A_period_that_ends_before_it_begins_is_not_sent()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<DexcomException>(() => Create().GetReadingsAsync(now, now.AddHours(-1)));

        Assert.Equal(DexcomError.Invalid, ex.Error);
        Assert.Empty(api.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Status_reads_both_the_healthy_and_the_503_answer(HttpStatusCode code, bool healthy)
    {
        var status = healthy ? "Healthy" : "Unhealthy";
        var reason = healthy ? "null" : "\"No reading for 20 minutes\"";
        api.Answer("/Health", $$"""
            {"status":"{{status}}","timestampUtc":"2026-10-03T19:24:25.6417454Z","latestReadingUtc":"2026-10-03T19:20:55.773Z","latestReadingAgeSeconds":209,"reason":{{reason}}}
            """, code);

        var result = await Create().GetStatusAsync();

        Assert.Equal(healthy, result.IsHealthy);
        Assert.Equal(status, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(209), result.LatestReadingAge);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 19, 20, 55, 773, TimeSpan.Zero), result.LatestReadingAt);
        Assert.Equal(healthy ? null : "No reading for 20 minutes", result.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, DexcomError.Unauthorized, "Blodsukkermåleren afviste nøglen.")]
    [InlineData(HttpStatusCode.NotFound, DexcomError.NoData, "Der er ingen målinger endnu.")]
    [InlineData(HttpStatusCode.NoContent, DexcomError.NoData, "Der er ingen målinger endnu.")]
    [InlineData(HttpStatusCode.InternalServerError, DexcomError.Failed, "Blodsukkermåleren svarede med en fejl.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, DexcomError.Failed, "Blodsukkermåleren svarede med en fejl.")]
    public async Task Failed_answers_become_a_short_danish_message(HttpStatusCode code, DexcomError error, string message)
    {
        api.Answer("/Dexcom/Latest", code == HttpStatusCode.NoContent ? "" : """{"title":"Error","detail":"Something in English"}""", code);

        var ex = await Assert.ThrowsAsync<DexcomException>(() => Create().GetLatestAsync());

        Assert.Equal(error, ex.Error);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task An_unreachable_api_is_offline()
    {
        api.Offline = true;

        var ex = await Assert.ThrowsAsync<DexcomException>(() => Create().GetLatestAsync());

        Assert.Equal(DexcomError.Offline, ex.Error);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"time":"2026-10-03T21:20:55+02:00","trend":"Flat"}""")]
    public async Task An_unreadable_answer_fails_with_a_message(string body)
    {
        api.Answer("/Dexcom/Latest", body);

        var ex = await Assert.ThrowsAsync<DexcomException>(() => Create().GetLatestAsync());

        Assert.Equal(DexcomError.Failed, ex.Error);
    }

    [Theory]
    [InlineData(" ", "https://dexcom.test")]
    [InlineData(Key, "")]
    [InlineData(Key, "ftp://dexcom.test")]
    public async Task Not_configured_means_no_calls(string apiKey, string baseUrl)
    {
        var service = Create(apiKey, baseUrl);

        Assert.False(service.IsConfigured);
        var ex = await Assert.ThrowsAsync<DexcomException>(() => service.GetLatestAsync());
        Assert.Equal(DexcomError.NotConfigured, ex.Error);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_base_url_with_a_path_keeps_the_path()
    {
        api.Answer("/glucose/Dexcom/Latest", """{"time":"2026-10-03T21:20:55+02:00","valueMmolL":5,"trend":"Flat"}""");

        await Create(baseUrl: "https://dexcom.test/glucose").GetLatestAsync();

        Assert.Equal("GET /glucose/Dexcom/Latest", Assert.Single(api.Requests).Line);
    }

    [Theory]
    [InlineData("DoubleUp", GlucoseTrend.DoubleUp)]
    [InlineData("fortyfivedown", GlucoseTrend.FortyFiveDown)]
    [InlineData("NotComputable", GlucoseTrend.NotComputable)]
    [InlineData("None", GlucoseTrend.Unknown)]
    [InlineData("3", GlucoseTrend.Unknown)]
    [InlineData(null, GlucoseTrend.Unknown)]
    public void Trends_are_read_by_name(string? text, GlucoseTrend trend) => Assert.Equal(trend, DexcomMapping.ToTrend(text));

    [Fact]
    public void Every_trend_has_danish_words()
    {
        Assert.Equal("Falder hurtigt", GlucoseTrend.SingleDown.Label());
        Assert.Equal("↘", GlucoseTrend.FortyFiveDown.Arrow());
        Assert.All(Enum.GetValues<GlucoseTrend>(), t => Assert.False(string.IsNullOrWhiteSpace(t.Label())));
    }

    [Fact]
    public void The_options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FamilyHub:Dexcom:BaseUrl"] = "https://dexcom.appcore.cc",
            ["FamilyHub:Dexcom:ApiKey"] = Key,
        }).Build();

        var options = configuration.GetSection(DexcomOptions.SectionName).Get<DexcomOptions>()!;

        Assert.True(options.IsConfigured);
        Assert.False(new DexcomOptions { BaseUrl = "https://dexcom.appcore.cc" }.IsConfigured);
    }

    private sealed class FixedOptions(DexcomOptions value) : IOptionsMonitor<DexcomOptions>
    {
        public DexcomOptions CurrentValue => value;

        public DexcomOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<DexcomOptions, string?> listener) => null;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Answers fixed JSON per path and query, like the real API.</summary>
    private sealed class FakeGlucoseApi : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Code)> answers = [];

        public List<(string Line, string? ApiKey)> Requests { get; } = [];

        public bool Offline { get; set; }

        public void Answer(string pathAndQuery, string body, HttpStatusCode code = HttpStatusCode.OK) => answers[pathAndQuery] = (body, code);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            request.Headers.TryGetValues(DexcomService.ApiKeyHeader, out var keys);
            Requests.Add(($"{request.Method} {path}", keys?.SingleOrDefault()));

            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            var (body, code) = answers.TryGetValue(path, out var answer) ? answer : ("""{"title":"Not Found"}""", HttpStatusCode.NotFound);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
