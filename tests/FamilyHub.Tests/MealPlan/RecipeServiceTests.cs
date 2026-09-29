using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyHub.Core.Configuration;
using FamilyHub.Core.Storage;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.MealPlan;

public sealed class RecipeServiceTests : IDisposable
{
    private const string Key = "test-key";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 9, 29, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeRecipeBook book = new();
    private readonly List<IDisposable> disposables = [];

    public RecipeServiceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }

        Directory.Delete(directory, recursive: true);
    }

    private RecipeService Create(string? apiKey = Key)
    {
        var paths = new AppDataPaths(Options.Create(new FamilyHubOptions { DataDirectory = directory }));
        var clock = new HubClock(time, Options.Create(new FamilyHubOptions()), NullLogger<HubClock>.Instance);
        disposables.Add(clock);
        var options = new FixedOptions(new RecipeApiOptions { BaseUrl = "https://opskrifter.test/api/v1", ApiKey = apiKey });
        var api = new RecipeApiClient(new FakeHttpClientFactory(book), options, NullLogger<RecipeApiClient>.Instance);
        return new RecipeService(api, paths, clock, NullLogger<RecipeService>.Instance);
    }

    [Fact]
    public async Task Refresh_fetches_every_page_of_the_book_sorted_danish()
    {
        book.AddRecipes(450);
        book.Add("Æblekage");
        var service = Create();

        Assert.True(await service.RefreshAsync());

        Assert.Equal(451, service.Recipes.Count);
        Assert.Equal("Æblekage", service.Recipes[^1].Title);
        Assert.Equal(3, book.Requests.Count(r => r.Url.Contains("/recipes?", StringComparison.Ordinal)));
        Assert.All(book.Requests, r => Assert.Equal(Key, r.ApiKey));
        Assert.Null(service.Status.Problem);
    }

    [Fact]
    public async Task The_copy_survives_a_restart_and_is_kept_when_the_book_cannot_be_reached()
    {
        book.Add("Boller");
        Assert.True(await Create().RefreshAsync());

        book.Offline = true;
        var afterRestart = Create();

        Assert.True(afterRestart.HasData);
        Assert.Equal("Boller", afterRestart.Recipes.Single().Title);
        Assert.False(await afterRestart.RefreshAsync());
        Assert.Equal(RecipeApiError.Offline, afterRestart.Status.Problem);
        Assert.Equal("Boller", afterRestart.Recipes.Single().Title);
    }

    [Fact]
    public async Task Ensure_fresh_only_fetches_when_the_copy_is_old()
    {
        var service = Create();
        await service.EnsureFreshAsync();
        var calls = book.Requests.Count;

        time.Advance(TimeSpan.FromMinutes(5));
        await service.EnsureFreshAsync();
        Assert.Equal(calls, book.Requests.Count);

        time.Advance(RecipeService.MaxAge);
        await service.EnsureFreshAsync();
        Assert.True(book.Requests.Count > calls);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent_and_the_status_says_so()
    {
        var service = Create(apiKey: " ");

        Assert.False(service.IsConfigured);
        Assert.False(await service.RefreshAsync());
        Assert.Equal(RecipeApiError.NotConfigured, service.Status.Problem);
        Assert.Empty(book.Requests);
    }

    [Fact]
    public async Task A_wrong_key_is_reported_as_unauthorized()
    {
        var service = Create(apiKey: "forkert");

        Assert.False(await service.RefreshAsync());
        Assert.Equal(RecipeApiError.Unauthorized, service.Status.Problem);
    }

    [Fact]
    public async Task Creating_a_recipe_adds_it_to_the_copy_and_tells_the_screens()
    {
        var service = Create();
        await service.RefreshAsync();
        var changes = 0;
        service.Changed += () => changes++;

        var created = await service.CreateRecipeAsync(new RecipeDraft
        {
            Title = "Pandekager",
            Category = "Aftensmad",
            Servings = 4,
            Difficulty = RecipeDifficulty.Easy,
            Ingredients = [new IngredientDraft { Amount = "5", Unit = "dl", Name = "mælk" }],
            Steps = ["Pisk", "Steg"],
        });

        Assert.Equal(created, service.Find(created.Id));
        Assert.Equal("Mor", created.Author);
        Assert.Equal(5, created.Ingredients.Single().Quantity);
        Assert.Equal("Aftensmad", service.Categories.Single().Name); // lookups refreshed after the write
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Updating_uses_put_and_fetches_the_recipe_when_the_answer_has_no_body()
    {
        var id = book.Add("Boller");
        book.PutReturnsNoContent = true;
        var service = Create();
        await service.RefreshAsync();

        var updated = await service.UpdateRecipeAsync(id, RecipeDraft.From(service.Find(id)!) with { Title = "Hveder" });

        Assert.Equal("Hveder", updated.Title);
        Assert.Equal("Hveder", service.Find(id)!.Title);
        Assert.Contains(book.Requests, r => r.Method == HttpMethod.Put && r.Url.EndsWith($"/recipes/{id}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_returns_the_recipe_so_it_can_be_restored()
    {
        var id = book.Add("Boller");
        var service = Create();
        await service.RefreshAsync();

        var deleted = await service.DeleteRecipeAsync(id);
        Assert.Empty(service.Recipes);

        var restored = await service.RestoreRecipeAsync(deleted!);
        Assert.NotEqual(id, restored.Id);
        Assert.Equal("Boller", service.Recipes.Single().Title);
    }

    [Fact]
    public async Task A_recipe_deleted_elsewhere_leaves_the_copy_when_reloaded()
    {
        var id = book.Add("Boller");
        var service = Create();
        await service.RefreshAsync();
        book.Remove(id);

        Assert.Null(await service.ReloadRecipeAsync(id));
        Assert.Null(service.Find(id));
    }

    [Fact]
    public async Task Renaming_a_category_renames_it_on_its_recipes()
    {
        var id = book.Add("Boller", category: "Brød");
        var service = Create();
        await service.RefreshAsync();
        var category = service.Categories.Single();

        await service.UpdateCategoryAsync(category.Id, new RecipeCategoryDraft { Name = "Bagværk" });

        Assert.Equal("Bagværk", service.Find(id)!.Category);
        Assert.Equal("Bagværk", service.Categories.Single().Name);
    }

    [Fact]
    public async Task A_category_name_in_use_gives_a_conflict_with_the_apis_message()
    {
        book.Add("Boller", category: "Brød");
        var service = Create();
        await service.RefreshAsync();

        var ex = await Assert.ThrowsAsync<RecipeApiException>(() => service.CreateCategoryAsync(new RecipeCategoryDraft { Name = "brød" }));

        Assert.Equal(RecipeApiError.Conflict, ex.Error);
        Assert.Equal("Kategorien findes allerede.", ex.Message);
    }

    [Fact]
    public async Task Photos_are_uploaded_as_multipart_and_the_new_url_is_kept()
    {
        var id = book.Add("Boller");
        var service = Create();
        await service.RefreshAsync();

        using var image = new MemoryStream([1, 2, 3]);
        var recipe = await service.SetRecipeImageAsync(id, image, "boller.png");

        Assert.Equal($"https://opskrifter.test/Recipes/Image?fileName={id}.png", recipe.ImageUrl);
        Assert.True(service.Find(id)!.HasImage);
        Assert.Contains(book.Requests, r => r.ContentType == "multipart/form-data" && r.Body.Contains("filename=boller.png", StringComparison.Ordinal));

        await service.RemoveRecipeImageAsync(id);
        Assert.False(service.Find(id)!.HasImage);
    }

    [Fact]
    public async Task Photos_in_other_formats_are_refused_before_sending()
    {
        var service = Create();
        using var image = new MemoryStream([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<RecipeApiException>(() => service.SetRecipeImageAsync(1, image, "boller.heic"));

        Assert.Equal(RecipeApiError.Invalid, ex.Error);
        Assert.Empty(book.Requests);
    }

    private sealed class FixedOptions(RecipeApiOptions value) : IOptionsMonitor<RecipeApiOptions>
    {
        public RecipeApiOptions CurrentValue => value;

        public RecipeApiOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<RecipeApiOptions, string?> listener) => null;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A tiny in-memory recipe book that answers like the real API.</summary>
    private sealed class FakeRecipeBook : HttpMessageHandler
    {
        private readonly Dictionary<int, JsonObject> recipes = [];
        private readonly Dictionary<int, string> categories = [];
        private int nextId = 1;

        public List<(HttpMethod Method, string Url, string? ApiKey, string? ContentType, string Body)> Requests { get; } = [];

        public bool Offline { get; set; }

        public bool PutReturnsNoContent { get; set; }

        public int Add(string title, string? category = null)
        {
            var id = nextId++;
            recipes[id] = Recipe(id, title, category is null ? null : CategoryId(category), 4, "Mor", []);
            return id;
        }

        public void AddRecipes(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Add($"Ret {i:000}");
            }
        }

        public void Remove(int id) => recipes.Remove(id);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues(RecipeApiClient.ApiKeyHeader, out var values) ? values.Single() : null;
            Requests.Add((request.Method, request.RequestUri!.ToString(), key, request.Content?.Headers.ContentType?.MediaType, body));

            if (Offline)
            {
                throw new HttpRequestException("No route to host");
            }

            if (key != Key)
            {
                return Json(new { title = "Manglende eller ugyldig API-nøgle", status = 401 }, HttpStatusCode.Unauthorized);
            }

            var path = request.RequestUri.AbsolutePath["/api/v1/".Length..].Split('/');
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            int? id = path.Length > 1 && int.TryParse(path[1], out var parsed) ? parsed : null;

            return (request.Method.Method, path[0], path.Length) switch
            {
                ("GET", "lookups", 1) => Json(new { difficulties = new[] { "Let", "Middel", "Svær" }, categories = CategoryList(), units = new[] { "dl" }, ingredients = Array.Empty<object>() }),
                ("GET", "recipes", 1) => Page(int.Parse(query["page"]!), int.Parse(query["pageSize"]!)),
                ("POST", "recipes", 1) => Json(Save(nextId++, JsonNode.Parse(body)!.AsObject()), HttpStatusCode.Created),
                ("GET", "recipes", 2) => recipes.TryGetValue(id!.Value, out var r) ? Json(r) : NotFound(),
                ("PUT", "recipes", 2) => Replace(id!.Value, body),
                ("DELETE", "recipes", 2) => recipes.Remove(id!.Value) ? new HttpResponseMessage(HttpStatusCode.NoContent) : NotFound(),
                ("PUT", "recipes", 3) => SetImage(id!.Value, $"https://opskrifter.test/Recipes/Image?fileName={id}.png"),
                ("DELETE", "recipes", 3) => SetImage(id!.Value, null),
                ("GET", "categories", 1) => Json(CategoryList()),
                ("GET", "categories", 2) => categories.ContainsKey(id!.Value) ? Json(CategoryList().Single(c => c.id == id)) : NotFound(),
                ("POST", "categories", 1) => CreateCategory(JsonNode.Parse(body)!["name"]!.GetValue<string>()),
                ("PUT", "categories", 2) => RenameCategory(id!.Value, JsonNode.Parse(body)!["name"]!.GetValue<string>()),
                _ => NotFound(),
            };
        }

        private HttpResponseMessage Page(int page, int pageSize)
        {
            var all = recipes.Values.OrderBy(r => r["title"]!.GetValue<string>(), StringComparer.Ordinal).ToList();
            var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Json(new { items, totalCount = all.Count, page, pageSize, totalPages = (all.Count + pageSize - 1) / pageSize });
        }

        private JsonObject Save(int id, JsonObject input)
        {
            var categoryId = input["categoryId"]?.GetValue<int>() ?? (input["category"]?.GetValue<string>() is { } name ? CategoryId(name) : (int?)null);
            var ingredients = (input["ingredients"]?.AsArray() ?? []).Select(i => (object)new
            {
                amount = i!["amount"]?.GetValue<string>() ?? "",
                quantity = double.TryParse(i["amount"]?.GetValue<string>(), out var q) ? q : (double?)null,
                unit = i["unit"]?.GetValue<string>() ?? "",
                name = i["name"]!.GetValue<string>(),
            }).ToList();
            var author = input["author"]?.GetValue<string>() ?? (recipes.TryGetValue(id, out var old) ? old["author"]!.GetValue<string>() : "Mor");
            var image = recipes.TryGetValue(id, out var existing) ? existing["imageUrl"]?.GetValue<string>() : null;
            recipes[id] = Recipe(id, input["title"]!.GetValue<string>(), categoryId, input["servings"]?.GetValue<int>() ?? 0, author, ingredients, image);
            return recipes[id];
        }

        private JsonObject Recipe(int id, string title, int? categoryId, int servings, string author, List<object> ingredients, string? imageUrl = null) =>
            JsonSerializer.SerializeToNode(new
            {
                id,
                title,
                categoryId,
                category = categoryId is { } c ? categories[c] : "",
                categoryIcon = "cookie",
                prepTimeMinutes = 0,
                cookTimeMinutes = 0,
                totalTimeMinutes = 0,
                servings,
                difficulty = "Middel",
                author,
                notes = "",
                lastModified = "2026-09-20T16:38:45.8986644",
                imageUrl,
                ingredients,
                steps = Array.Empty<string>(),
            })!.AsObject();

        private HttpResponseMessage SetImage(int id, string? url)
        {
            if (!recipes.TryGetValue(id, out var recipe))
            {
                return NotFound();
            }

            recipe["imageUrl"] = url;
            return url is null ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(new { imageUrl = url });
        }

        private int CategoryId(string name)
        {
            var existing = categories.FirstOrDefault(c => string.Equals(c.Value, name, StringComparison.OrdinalIgnoreCase));
            if (existing.Value is not null)
            {
                return existing.Key;
            }

            var id = categories.Count + 1;
            categories[id] = name;
            return id;
        }

        private HttpResponseMessage CreateCategory(string name) =>
            categories.Values.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
                ? Json(new { title = "Conflict", status = 409, detail = "Kategorien findes allerede." }, HttpStatusCode.Conflict)
                : Json(new { id = CategoryId(name), name, icon = "cookie", recipeCount = 0 }, HttpStatusCode.Created);

        private HttpResponseMessage RenameCategory(int id, string name)
        {
            categories[id] = name;
            foreach (var recipe in recipes.Values.Where(r => r["categoryId"]?.GetValue<int>() == id))
            {
                recipe["category"] = name;
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private HttpResponseMessage Replace(int id, string body)
        {
            if (!recipes.ContainsKey(id))
            {
                return NotFound();
            }

            var saved = Save(id, JsonNode.Parse(body)!.AsObject());
            return PutReturnsNoContent ? new HttpResponseMessage(HttpStatusCode.NoContent) : Json(saved);
        }

        private List<CategoryJson> CategoryList() =>
            [.. categories.Select(c => new CategoryJson(c.Key, c.Value, "cookie", recipes.Values.Count(r => r["categoryId"]?.GetValue<int>() == c.Key)))];

        private sealed record CategoryJson(int id, string name, string icon, int recipeCount);

        private static HttpResponseMessage NotFound() => Json(new { title = "Not Found", status = 404 }, HttpStatusCode.NotFound);

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
