using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyHub.Core.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.Core;

public sealed class PrintServiceTests
{
    private const string Key = "ak_test";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly FakePrintServer server = new();

    private PrintService Create(string? apiKey = Key, int? printerId = null, string baseUrl = "http://print.test:8080")
    {
        var options = new FixedOptions(new PrintApiOptions { BaseUrl = baseUrl, ApiKey = apiKey, PrinterId = printerId });
        var api = new PrintApiClient(new FakeHttpClientFactory(server), options, NullLogger<PrintApiClient>.Instance);
        return new PrintService(api, options, time, NullLogger<PrintService>.Instance);
    }

    [Fact]
    public async Task Print_sends_the_pdf_as_base64_with_the_options_and_the_key()
    {
        server.AddPrinter(3, "Køkkenprinter");
        var service = Create(printerId: 3);

        var job = await service.PrintPdfAsync(Pdf, new PrintOptions { Copies = 2, Duplex = true, Pages = " 1-2 " });

        var submit = Assert.Single(server.Requests);
        Assert.Equal("POST /Print/Submit", submit.Line);
        Assert.Equal(Key, submit.ApiKey);
        var body = JsonNode.Parse(submit.Body)!;
        Assert.Equal(3, (int)body["printerId"]!);
        Assert.Equal(Pdf, Convert.FromBase64String((string)body["documentBase64"]!));
        Assert.Equal(2, (int)body["copies"]!);
        Assert.False((bool)body["color"]!);
        Assert.True((bool)body["duplex"]!);
        Assert.Equal("1-2", (string)body["pages"]!);

        Assert.Equal(PrintJobStatus.Queued, job.Status);
        Assert.Equal("I kø", job.StatusText);
        Assert.Equal("Køkkenprinter", job.PrinterName);
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), job.CreatedAt);
        Assert.Equal(job.Id, Assert.Single(service.ActiveJobs).Id);
    }

    [Fact]
    public async Task Without_a_chosen_printer_the_only_printer_on_the_server_is_used()
    {
        server.AddPrinter(7, "HP");
        var service = Create();

        var job = await service.PrintPdfAsync(Pdf);

        Assert.Equal(7, job.PrinterId);
        Assert.Equal(["GET /Health", "POST /Print/Submit"], server.Requests.Select(r => r.Line));
    }

    [Fact]
    public async Task Without_a_chosen_printer_it_does_not_guess_between_several()
    {
        server.AddPrinter(1, "Kontor");
        server.AddPrinter(2, "Køkken");
        var service = Create();

        var ex = await Assert.ThrowsAsync<PrintApiException>(() => service.PrintPdfAsync(Pdf));

        Assert.Equal(PrintApiError.NoPrinter, ex.Error);
        Assert.DoesNotContain(server.Requests, r => r.Line.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not a pdf", 1, null, "Kun PDF-dokumenter kan udskrives.")]
    [InlineData("", 1, null, "Dokumentet er tomt.")]
    [InlineData("%PDF-1.4", 0, null, "Antal kopier skal være mellem 1 og 99.")]
    [InlineData("%PDF-1.4", 100, null, "Antal kopier skal være mellem 1 og 99.")]
    [InlineData("%PDF-1.4", 1, "5-2", "Skriv den laveste side først, fx 2-5.")]
    [InlineData("%PDF-1.4", 1, "-3", "Skriv siderne som fx 2-5, 1,3 eller 3- (side 3 og frem).")]
    public async Task Documents_and_options_the_server_would_refuse_are_stopped_before_sending(string document, int copies, string? pages, string message)
    {
        server.AddPrinter(1, "HP");
        var service = Create(printerId: 1);

        var ex = await Assert.ThrowsAsync<PrintApiException>(() =>
            service.PrintPdfAsync(Encoding.ASCII.GetBytes(document), new PrintOptions { Copies = copies, Pages = pages }));

        Assert.Equal(PrintApiError.Invalid, ex.Error);
        Assert.Equal(message, ex.Message);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2-9")]
    [InlineData("5")]
    [InlineData("1,3,5-7")]
    [InlineData("3-")]
    [InlineData(" 1 - 2 , 4 ")]
    public void Valid_page_selections_pass(string? pages) => Assert.Null(PrintValidation.ValidatePages(pages));

    [Fact]
    public async Task Not_configured_means_no_calls()
    {
        var service = Create(apiKey: " ");

        Assert.False(service.IsConfigured);
        var ex = await Assert.ThrowsAsync<PrintApiException>(() => service.PrintPdfAsync(Pdf, new PrintOptions { PrinterId = 1 }));
        Assert.Equal(PrintApiError.NotConfigured, ex.Error);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Answers_from_the_server_become_short_danish_errors()
    {
        server.AddPrinter(1, "HP");

        var wrongKey = await Assert.ThrowsAsync<PrintApiException>(() => Create(apiKey: "ak_wrong").PrintPdfAsync(Pdf, new PrintOptions { PrinterId = 1 }));
        Assert.Equal(PrintApiError.Unauthorized, wrongKey.Error);
        Assert.Equal("Printserveren afviste nøglen.", wrongKey.Message);

        var unknownPrinter = await Assert.ThrowsAsync<PrintApiException>(() => Create().PrintPdfAsync(Pdf, new PrintOptions { PrinterId = 99 }));
        Assert.Equal(PrintApiError.NotFound, unknownPrinter.Error);
        Assert.Equal("Printeren findes ikke på printserveren.", unknownPrinter.Message);

        server.Offline = true;
        var offline = await Assert.ThrowsAsync<PrintApiException>(() => Create().PrintPdfAsync(Pdf, new PrintOptions { PrinterId = 1 }));
        Assert.Equal(PrintApiError.Offline, offline.Error);
    }

    [Fact]
    public async Task The_health_check_lists_the_printers_even_when_none_are_online()
    {
        server.AddPrinter(1, "HP", isOnline: false);

        var status = await Create().GetStatusAsync();   // 503 from the print server

        Assert.False(status.IsHealthy);
        Assert.Equal("Printeren svarer ikke. Er den tændt?", Assert.Single(status.Printers).Problem);
    }

    [Fact]
    public async Task A_printer_that_needs_attention_says_why_in_danish()
    {
        server.AddPrinter(2, "Køkken", reasons: ["media-empty-error", "marker-supply-low-warning"], state: "stopped");

        var printer = (await Create().GetStatusAsync()).Find(2)!;

        Assert.Equal("Printeren mangler papir.", printer.Problem);
        Assert.False(printer.IsReady);
    }

    [Fact]
    public async Task Followed_jobs_tell_the_screens_until_they_are_finished()
    {
        server.AddPrinter(1, "HP");
        var service = Create(printerId: 1);
        var changes = new List<PrintJob>();
        service.JobChanged += changes.Add;

        var job = await service.PrintPdfAsync(Pdf);
        Assert.Single(changes);

        // Nothing new on the print server: no event.
        Assert.True(await service.RefreshActiveJobsAsync(CancellationToken.None));
        Assert.Single(changes);

        server.SetJob(job.Id, "Printing", printerJobState: "processing-stopped", reasons: ["printer-stopped", "media-empty-error"]);
        Assert.True(await service.RefreshActiveJobsAsync(CancellationToken.None));
        Assert.Equal("Printeren mangler papir.", changes[^1].Problem);
        Assert.Equal("Udskriver", changes[^1].StatusText);

        server.SetJob(job.Id, "Completed", printerJobState: "completed");
        Assert.False(await service.RefreshActiveJobsAsync(CancellationToken.None));
        Assert.Equal(PrintJobStatus.Completed, changes[^1].Status);
        Assert.True(changes[^1].IsFinished);
        Assert.Null(changes[^1].Problem);
        Assert.Empty(service.ActiveJobs);

        var calls = server.Requests.Count;
        Assert.False(await service.RefreshActiveJobsAsync(CancellationToken.None));
        Assert.Equal(calls, server.Requests.Count);
    }

    [Fact]
    public async Task A_job_is_no_longer_followed_after_an_hour()
    {
        server.AddPrinter(1, "HP");
        var service = Create(printerId: 1);
        await service.PrintPdfAsync(Pdf);

        time.Advance(PrintService.FollowFor + TimeSpan.FromMinutes(1));

        Assert.False(await service.RefreshActiveJobsAsync(CancellationToken.None));
        Assert.Empty(service.ActiveJobs);
    }

    [Fact]
    public async Task Cancel_stops_following_and_a_finished_job_cannot_be_cancelled()
    {
        server.AddPrinter(1, "HP");
        var service = Create(printerId: 1);
        var job = await service.PrintPdfAsync(Pdf);

        var cancelled = await service.CancelAsync(job.Id);

        Assert.Equal(PrintJobStatus.Canceled, cancelled.Status);
        Assert.Empty(service.ActiveJobs);
        var again = await Assert.ThrowsAsync<PrintApiException>(() => service.CancelAsync(job.Id));
        Assert.Equal(PrintApiError.Conflict, again.Error);
        Assert.Equal("Udskriften er allerede færdig.", again.Message);
        Assert.Null(await service.GetJobAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_job_list_is_asked_for_with_the_filters()
    {
        server.AddPrinter(1, "HP");
        var service = Create(printerId: 1);
        await service.PrintPdfAsync(Pdf);

        var page = await service.GetJobsAsync(new PrintJobQuery { PrinterId = 1, Status = PrintJobStatus.Queued, PageSize = 1000 });

        Assert.Equal("GET /Print/GetAll?PrinterId=1&Status=Queued&Page=1&PageSize=500", server.Requests[^1].Line);
        Assert.Single(page.Items);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData(new[] { "none" }, null)]
    [InlineData(new[] { "media-jam-error", "media-empty-error" }, "Der sidder papir fast i printeren.")]
    [InlineData(new[] { "cover-open-warning" }, "Et låg på printeren står åbent.")]
    [InlineData(new[] { "marker-supply-low-report" }, null)]
    [InlineData(new[] { "MARKER-SUPPLY-LOW-WARNING" }, "Blækket er ved at slippe op.")]
    public void Printer_reasons_are_described_in_danish(string[] reasons, string? expected) =>
        Assert.Equal(expected, PrintTexts.DescribeReasons(reasons));

    [Fact]
    public void A_job_waiting_for_an_unreachable_printer_says_so()
    {
        var job = new PrintJob { Status = PrintJobStatus.Queued, Attempts = 2 };

        Assert.Equal("Printeren svarer ikke. Prøver igen om lidt.", job.Problem);
    }

    [Fact]
    public void Settings_from_env_bind_with_an_empty_printer_id()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FamilyHub:Printing:BaseUrl"] = "http://homelab.local:8080",
            ["FamilyHub:Printing:ApiKey"] = Key,
            ["FamilyHub:Printing:PrinterId"] = "",
        }).Build();

        var options = configuration.GetSection(PrintApiOptions.SectionName).Get<PrintApiOptions>()!;

        Assert.Null(options.PrinterId);
        Assert.True(options.IsConfigured);
        Assert.False(new PrintApiOptions { ApiKey = Key }.IsConfigured);
    }

    private sealed class FixedOptions(PrintApiOptions value) : IOptionsMonitor<PrintApiOptions>
    {
        public PrintApiOptions CurrentValue => value;

        public PrintApiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PrintApiOptions, string?> listener) => null;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A tiny print server that answers like the real Print API.</summary>
    private sealed class FakePrintServer : HttpMessageHandler
    {
        private readonly List<JsonObject> printers = [];
        private readonly Dictionary<Guid, JsonObject> jobs = [];

        public List<(string Line, string? ApiKey, string Body)> Requests { get; } = [];

        public bool Offline { get; set; }

        public void AddPrinter(int id, string name, bool isOnline = true, string[]? reasons = null, string? state = "idle") =>
            printers.Add(new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["isOnline"] = isOnline,
                ["state"] = isOnline ? state : null,
                ["stateReasons"] = new JsonArray([.. (reasons ?? ["none"]).Select(r => JsonValue.Create(r))]),
                ["isAcceptingJobs"] = isOnline ? true : null,
                ["error"] = isOnline ? null : "No answer within 3 s.",
            });

        public void SetJob(Guid id, string status, string? printerJobState = null, string[]? reasons = null)
        {
            var job = jobs[id];
            job["status"] = status;
            job["isFinished"] = status is "Completed" or "Failed" or "Canceled";
            job["printerJobState"] = printerJobState;
            job["printerJobStateReasons"] = new JsonArray([.. (reasons ?? []).Select(r => JsonValue.Create(r))]);
            job["updatedAt"] = "2026-10-01T08:00:" + Random.Shared.Next(10, 59);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            request.Headers.TryGetValues(PrintApiClient.ApiKeyHeader, out var keys);
            var apiKey = keys?.SingleOrDefault();
            Requests.Add(($"{request.Method} {path}", apiKey, body));

            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            if (path == "/Health")
            {
                var online = printers.Count(p => (bool)p["isOnline"]!);
                return Json(online > 0 ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, new JsonObject
                {
                    ["status"] = online > 0 ? "Healthy" : "Unhealthy",
                    ["timestampUtc"] = "2026-10-01T08:00:00Z",
                    ["printersOnline"] = online,
                    ["printersTotal"] = printers.Count,
                    ["printers"] = new JsonArray([.. printers.Select(p => p.DeepClone())]),
                });
            }

            if (apiKey != Key)
            {
                return Problem(HttpStatusCode.Unauthorized, "Missing or invalid API key.");
            }

            var segments = request.RequestUri.AbsolutePath.Trim('/').Split('/');
            switch (request.Method.Method, segments)
            {
                case ("POST", ["Print", "Submit"]):
                {
                    var input = JsonNode.Parse(body)!;
                    var printerId = (int)input["printerId"]!;
                    if (printers.FirstOrDefault(p => (int)p["id"]! == printerId) is not { } printer)
                    {
                        return Problem(HttpStatusCode.NotFound, $"No printer is registered with id {printerId}.");
                    }

                    var id = Guid.NewGuid();
                    jobs[id] = new JsonObject
                    {
                        ["id"] = id,
                        ["printerId"] = printerId,
                        ["printerName"] = (string)printer["name"]!,
                        ["documentSizeBytes"] = Convert.FromBase64String((string)input["documentBase64"]!).Length,
                        ["pageCount"] = 2,
                        ["pages"] = (string?)input["pages"],
                        ["pagesToPrint"] = 2,
                        ["copies"] = (int)input["copies"]!,
                        ["color"] = (bool)input["color"]!,
                        ["duplex"] = (bool)input["duplex"]!,
                        ["status"] = "Queued",
                        ["isFinished"] = false,
                        ["attempts"] = 0,
                        ["printerJobStateReasons"] = new JsonArray(),
                        ["submittedBy"] = "Family Hub",
                        ["createdAt"] = "2026-10-01T08:00:00",
                        ["updatedAt"] = "2026-10-01T08:00:00",
                    };
                    return Json(HttpStatusCode.Accepted, jobs[id]);
                }

                case ("GET", ["Print", "GetStatus", var id]):
                    return jobs.TryGetValue(Guid.Parse(id), out var job) ? Json(HttpStatusCode.OK, job) : Problem(HttpStatusCode.NotFound, null);

                case ("POST", ["Print", "Cancel", var id]):
                {
                    if (!jobs.TryGetValue(Guid.Parse(id), out var cancel))
                    {
                        return Problem(HttpStatusCode.NotFound, null);
                    }

                    if ((bool)cancel["isFinished"]!)
                    {
                        return Problem(HttpStatusCode.Conflict, $"The job is already {cancel["status"]}.");
                    }

                    SetJob(Guid.Parse(id), "Canceled");
                    return Json(HttpStatusCode.OK, cancel);
                }

                case ("GET", ["Print", "GetAll"]):
                    return Json(HttpStatusCode.OK, new JsonObject
                    {
                        ["items"] = new JsonArray([.. jobs.Values.Select(j => j.DeepClone())]),
                        ["totalCount"] = jobs.Count,
                        ["page"] = 1,
                        ["pageSize"] = 500,
                    });

                default:
                    return Problem(HttpStatusCode.NotFound, null);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(HttpStatusCode status, string? detail) =>
            new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { title = status.ToString(), status = (int)status, detail }), Encoding.UTF8, "application/problem+json"),
            };
    }
}
