using System.Net;
using System.Text;
using FamilyHub.Core.Mambeno;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyHub.Tests.Core;

public sealed class MambenoServiceTests
{
    private const string Key = "ak_test";

    // Shortened from a real answer (2026-10-05).
    private const string RecipeJson = """
        {"id":2992,"title":"Afrikansk gryderet med søde kartofler","categoryId":16,"category":"Mindre kød og vegetar","categoryIcon":"cookie",
         "prepTimeMinutes":35,"cookTimeMinutes":0,"totalTimeMinutes":35,"servings":4,"difficulty":null,"author":"Mambeno",
         "notes":"Lad børnene skære kartoflerne.","lastModified":"2022-08-18T14:28:19Z","imageUrl":null,
         "ingredients":[
           {"amount":"250","quantity":250,"unit":"gram","name":"ris","group":null,"linkedRecipeId":null},
           {"amount":"0.5","quantity":0.5,"unit":"spsk","name":"paprika","group":"Krydderier","linkedRecipeId":null},
           {"amount":"6","quantity":6,"unit":"stk","name":"færdige muffins","group":null,"linkedRecipeId":1717},
           {"amount":"","quantity":null,"unit":"","name":"salt og peber","group":null,"linkedRecipeId":null},
           {"amount":"1","quantity":1,"unit":"stk","name":" ","group":null,"linkedRecipeId":null}],
         "steps":["Kog risene.","  ","Svits løget."],
         "description":"En mild gryderet.",
         "categories":[{"id":18,"name":"Aftensmad","icon":"cookie"},{"id":16,"name":"Mindre kød og vegetar","icon":"cookie"}],
         "sourceUrl":"https://mambeno.dk/opskrifter/afrikansk-gryderet/"}
        """;

    private readonly FakeMambenoApi api = new();

    private MambenoService Create(string? apiKey = Key, string baseUrl = "https://mambeno.test") =>
        new(new FakeHttpClientFactory(api), new FixedOptions(new MambenoOptions { BaseUrl = baseUrl, ApiKey = apiKey }), NullLogger<MambenoService>.Instance);

    [Fact]
    public async Task A_recipe_is_read_with_every_field()
    {
        api.Answer("/api/v1/recipes/2992", RecipeJson);

        var recipe = await Create().GetRecipeAsync(2992);

        var request = Assert.Single(api.Requests);
        Assert.Equal("GET /api/v1/recipes/2992", request.Line);
        Assert.Equal(Key, request.ApiKey);
        Assert.NotNull(recipe);
        Assert.Equal("Afrikansk gryderet med søde kartofler", recipe.Title);
        Assert.Equal("En mild gryderet.", recipe.Description);
        Assert.Equal(16, recipe.CategoryId);
        Assert.Equal([new MambenoCategoryRef(18, "Aftensmad"), new MambenoCategoryRef(16, "Mindre kød og vegetar")], recipe.Categories);
        Assert.Equal(35, recipe.TotalTimeMinutes);
        Assert.True(recipe.HasTime);
        Assert.True(recipe.CanScale);
        Assert.False(recipe.HasImage);
        Assert.Equal(new DateTimeOffset(2022, 8, 18, 14, 28, 19, TimeSpan.Zero), recipe.LastModified);
        Assert.Equal("https://mambeno.dk/opskrifter/afrikansk-gryderet/", recipe.SourceUrl);
        Assert.Equal(["Kog risene.", "Svits løget."], recipe.Steps);

        // The line without a name is dropped.
        Assert.Equal(["ris", "paprika", "færdige muffins", "salt og peber"], recipe.Ingredients.Select(i => i.Name));
        Assert.Equal(0.5, recipe.Ingredients[1].Quantity);
        Assert.Equal("Krydderier", recipe.Ingredients[1].Group);
        Assert.Equal(1717, recipe.Ingredients[2].LinkedRecipeId);
        Assert.Null(recipe.Ingredients[3].Quantity);
        Assert.Equal("", recipe.Ingredients[3].Unit);
    }

    [Fact]
    public async Task A_recipe_or_category_that_does_not_exist_is_null()
    {
        api.Answer("/api/v1/recipes/9", """{"title":"Not Found","status":404}""", HttpStatusCode.NotFound);
        api.Answer("/api/v1/categories/9", """{"title":"Not Found","status":404}""", HttpStatusCode.NotFound);

        Assert.Null(await Create().GetRecipeAsync(9));
        Assert.Null(await Create().GetCategoryAsync(9));
    }

    [Fact]
    public async Task Categories_keep_their_place_in_the_tree()
    {
        api.Answer("/api/v1/categories", """
            [{"id":18,"name":"Aftensmad","icon":"cookie","recipeCount":3206,"parentId":null},
             {"id":35,"name":"Arabisk","icon":"cookie","recipeCount":40,"parentId":18},
             {"id":7,"name":"Billigste opskrifter","icon":"cookie","recipeCount":0,"parentId":null},
             {"id":null,"name":"Uden id"},
             {"id":5,"name":" "}]
            """);

        var categories = await Create().GetCategoriesAsync();

        Assert.Equal(["Aftensmad", "Arabisk", "Billigste opskrifter"], categories.Select(c => c.Name));
        Assert.True(categories[0].IsTopLevel);
        Assert.Equal(18, categories[1].ParentId);
        Assert.Equal(3206, categories[0].RecipeCount);
    }

    [Fact]
    public async Task A_search_sends_every_filter_and_reads_the_page()
    {
        var query = new MambenoQuery
        {
            Search = "kylling karry",
            CategoryId = 18,
            MaxTotalMinutes = 30,
            Ingredients = ["kylling", " ", "ris"],
            ModifiedSince = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(2)),
            Sort = MambenoSort.TotalTime,
            Page = 2,
            PageSize = 500,
        };
        api.Answer(
            "/api/v1/recipes?search=kylling%20karry&categoryId=18&maxTotalMinutes=30&ingredient=kylling&ingredient=ris" +
            "&modifiedSince=2026-10-01T06%3A00%3A00Z&sort=TotalTime&page=2&pageSize=200",
            $$"""{"items":[{{RecipeJson}},{"id":1,"title":null}],"totalCount":401,"page":2,"pageSize":200,"totalPages":3}""");

        var page = await Create().SearchRecipesAsync(query);

        Assert.Equal(2992, Assert.Single(page.Items).Id);
        Assert.Equal(401, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void A_plain_search_only_sends_paging()
    {
        Assert.Equal("?page=1&pageSize=50", MambenoMapping.ToQueryString(new MambenoQuery()));
        Assert.Equal("?category=Fisk&page=1&pageSize=1", MambenoMapping.ToQueryString(new MambenoQuery { Category = "Fisk", MaxTotalMinutes = 0, Page = 0, PageSize = 0 }));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Status_reads_both_the_healthy_and_the_503_answer(HttpStatusCode code, bool healthy)
    {
        var status = healthy ? "Healthy" : "Unhealthy";
        var message = healthy ? "null" : "\"Data is 9 days old\"";
        api.Answer("/Health", $$"""
            {"status":"{{status}}","timestampUtc":"2026-10-05T10:04:12.5794995Z","dataLastUpdatedUtc":"2026-10-05T09:57:39Z","message":{{message}}}
            """, code);

        var result = await Create().GetStatusAsync();

        Assert.Equal(healthy, result.IsHealthy);
        Assert.Equal(status, result.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 57, 39, TimeSpan.Zero), result.DataLastUpdatedAt);
        Assert.Equal(healthy ? null : "Data is 9 days old", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MambenoError.Unauthorized, "Mambeno afviste nøglen.")]
    [InlineData(HttpStatusCode.BadRequest, MambenoError.Invalid, "Mambeno kunne ikke bruge søgningen.")]
    [InlineData(HttpStatusCode.NotFound, MambenoError.Failed, "Mambeno svarede med en fejl.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, MambenoError.Failed, "Mambeno svarede med en fejl.")]
    public async Task Failed_answers_become_a_short_danish_message(HttpStatusCode code, MambenoError error, string message)
    {
        api.Answer("/api/v1/categories", """{"title":"Error","detail":"A valid x-api-key header is required."}""", code);

        var ex = await Assert.ThrowsAsync<MambenoException>(() => Create().GetCategoriesAsync());

        Assert.Equal(error, ex.Error);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task An_unreachable_api_is_offline()
    {
        api.Offline = true;

        var ex = await Assert.ThrowsAsync<MambenoException>(() => Create().GetRecipeAsync(1));

        Assert.Equal(MambenoError.Offline, ex.Error);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"id":2992,"title":""}""")]
    public async Task An_unreadable_answer_fails_with_a_message(string body)
    {
        api.Answer("/api/v1/recipes/2992", body);

        var ex = await Assert.ThrowsAsync<MambenoException>(() => Create().GetRecipeAsync(2992));

        Assert.Equal(MambenoError.Failed, ex.Error);
    }

    [Theory]
    [InlineData(" ", "https://mambeno.test")]
    [InlineData(Key, "")]
    [InlineData(Key, "ftp://mambeno.test")]
    public async Task Not_configured_means_no_calls(string apiKey, string baseUrl)
    {
        var service = Create(apiKey, baseUrl);

        Assert.False(service.IsConfigured);
        var ex = await Assert.ThrowsAsync<MambenoException>(() => service.GetCategoriesAsync());
        Assert.Equal(MambenoError.NotConfigured, ex.Error);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_base_url_with_a_path_keeps_the_path()
    {
        api.Answer("/mambeno/Health", """{"status":"Healthy"}""");

        await Create(baseUrl: "https://mambeno.test/mambeno").GetStatusAsync();

        Assert.Equal("GET /mambeno/Health", Assert.Single(api.Requests).Line);
    }

    [Fact]
    public void The_options_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FamilyHub:Mambeno:BaseUrl"] = "https://mambeno.appcore.cc",
            ["FamilyHub:Mambeno:ApiKey"] = Key,
        }).Build();

        var options = configuration.GetSection(MambenoOptions.SectionName).Get<MambenoOptions>()!;

        Assert.True(options.IsConfigured);
        Assert.False(new MambenoOptions { BaseUrl = "https://mambeno.appcore.cc" }.IsConfigured);
    }

    private sealed class FixedOptions(MambenoOptions value) : IOptionsMonitor<MambenoOptions>
    {
        public MambenoOptions CurrentValue => value;

        public MambenoOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<MambenoOptions, string?> listener) => null;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Answers fixed JSON per path and query, like the real API.</summary>
    private sealed class FakeMambenoApi : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Body, HttpStatusCode Code)> answers = [];

        public List<(string Line, string? ApiKey)> Requests { get; } = [];

        public bool Offline { get; set; }

        public void Answer(string pathAndQuery, string body, HttpStatusCode code = HttpStatusCode.OK) => answers[pathAndQuery] = (body, code);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            request.Headers.TryGetValues(MambenoService.ApiKeyHeader, out var keys);
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
