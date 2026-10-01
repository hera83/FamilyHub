using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Printing;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Print;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace FamilyHub.Tests.MealPlan;

public sealed class RecipePrintTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static readonly Recipe Lasagne = new()
    {
        Id = 5,
        Title = "Lasagne med æblesalat",
        Category = "Aftensmad",
        CategoryId = 1,
        TotalTimeMinutes = 75,
        Servings = 4,
        Difficulty = RecipeDifficulty.Medium,
        Author = "Mor",
        Notes = "Kan fryses.\nTag den op dagen før.",
        ImageUrl = "https://opskrifter.test/billeder/lasagne.jpg",
        Ingredients =
        [
            new() { Amount = "500", Quantity = 500, Unit = "g", Name = "hakket oksekød" },
            new() { Amount = "1,5", Quantity = 1.5, Unit = "dl", Name = "fløde" },
            new() { Name = "Æblesalat:" },
            new() { Amount = "2", Quantity = 2, Name = "æbler" },
        ],
        Steps = ["Brun kødet.", "Lag pasta, kødsovs og bechamel.\nSlut med ost.", "Bag 45 min ved 200 °C."],
    };

    private readonly FakeServer server = new();
    private readonly HubClock clock = new(TimeProvider.System, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);

    public void Dispose() => clock.Dispose();

    [Fact]
    public void A_recipe_becomes_a_pdf_with_or_without_a_photo()
    {
        Assert.StartsWith("%PDF-", Ascii(RecipePdf.Create(Lasagne, Photo(), Today)));
        Assert.StartsWith("%PDF-", Ascii(RecipePdf.Create(Lasagne, null, Today)));
        Assert.StartsWith("%PDF-", Ascii(RecipePdf.Create(Lasagne, "not an image"u8.ToArray(), Today)));
        Assert.StartsWith("%PDF-", Ascii(RecipePdf.Create(new Recipe { Id = 9, Title = "Tom" }, null, Today)));
    }

    [Fact]
    public void A_long_recipe_continues_on_the_next_page()
    {
        var shortRecipe = RecipePdf.Compose(Lasagne, null, Today).GenerateImages().Count();
        var longRecipe = RecipePdf.Compose(Lasagne with { Steps = [.. Enumerable.Range(1, 40).Select(i => $"Trin {i}: rør godt rundt, og lad det simre et par minutter, til det har sat sig.")] }, null, Today)
            .GenerateImages().Count();

        Assert.Equal(1, shortRecipe);
        Assert.True(longRecipe > 1);
    }

    [Fact]
    public async Task Printing_fetches_the_photo_and_prints_in_colour()
    {
        server.Photo = Photo();

        var job = await CreatePrinter().PrintAsync(Lasagne);

        Assert.Equal(PrintJobStatus.Queued, job.Status);
        Assert.Contains(server.Requests, r => r == "GET /billeder/lasagne.jpg");
        var submit = Assert.Single(server.Submitted);
        Assert.True((bool)submit["color"]!);
        Assert.Equal(3, (int)submit["printerId"]!);
    }

    [Fact]
    public async Task A_missing_photo_does_not_stop_the_print()
    {
        server.Photo = null;   // 404

        await CreatePrinter().PrintAsync(Lasagne);

        Assert.False((bool)Assert.Single(server.Submitted)["color"]!);
    }

    private RecipePrinter CreatePrinter()
    {
        var options = new FixedOptions(new PrintApiOptions { BaseUrl = "http://print.test:8080", ApiKey = "ak_test", PrinterId = 3 });
        var http = new Http(server);
        var api = new PrintApiClient(http, options, NullLogger<PrintApiClient>.Instance);
        var printing = new PrintService(api, options, TimeProvider.System, NullLogger<PrintService>.Instance);
        return new RecipePrinter(printing, http, clock, NullLogger<RecipePrinter>.Instance);
    }

    /// <summary>A real PNG, drawn by QuestPDF itself.</summary>
    private static byte[] Photo()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        return Document.Create(d => d.Page(p =>
        {
            p.Size(200, 120);
            p.Content().Background(Colors.Orange.Medium);
        })).GenerateImages().Single();
    }

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes, 0, 5);

    private sealed class FixedOptions(PrintApiOptions value) : IOptionsMonitor<PrintApiOptions>
    {
        public PrintApiOptions CurrentValue => value;

        public PrintApiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PrintApiOptions, string?> listener) => null;
    }

    private sealed class Http(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>The recipe book's photo and the print server's Submit in one.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public byte[]? Photo { get; set; }

        public List<string> Requests { get; } = [];

        public List<JsonNode> Submitted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.RequestUri.Host == "opskrifter.test")
            {
                return Photo is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Photo) };
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Submitted.Add(body);
            var job = new JsonObject
            {
                ["id"] = Guid.NewGuid(),
                ["printerId"] = (int)body["printerId"]!,
                ["status"] = "Queued",
                ["createdAt"] = "2026-10-01T08:00:00",
                ["updatedAt"] = "2026-10-01T08:00:00",
            };
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(job.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
