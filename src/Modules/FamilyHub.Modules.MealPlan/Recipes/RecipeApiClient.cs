using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// Every call in the recipe book's API (<c>/api/v1</c>), one method per endpoint. Plain HTTP + JSON.
/// Throws <see cref="RecipeApiException"/> with a Danish message; "get one" returns null for 404.
/// Screens normally use <see cref="RecipeService"/> (local copy, works offline) – this is the raw access.
/// See docs/opskrifter-api.md.
/// </summary>
public sealed class RecipeApiClient(IHttpClientFactory httpClients, IOptionsMonitor<RecipeApiOptions> options, ILogger<RecipeApiClient> logger)
{
    public const string HttpClientName = "FamilyHub.Recipes";
    public const string ApiKeyHeader = "X-Api-Key";

    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    // ------------------------------------------------------------------ recipes

    /// <summary><c>GET /recipes</c> – one page of recipes matching the filters.</summary>
    public async Task<RecipePage> SearchRecipesAsync(RecipeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await SendRequiredAsync<RecipePageDto>(HttpMethod.Get, "recipes" + ToQueryString(query), null, cancellationToken);
        return RecipeMapping.ToPage(page);
    }

    /// <summary>Every recipe matching the filters (follows the pages, 200 at a time). The whole book: <c>GetAllRecipesAsync()</c>.</summary>
    public async Task<IReadOnlyList<Recipe>> GetAllRecipesAsync(RecipeQuery? query = null, CancellationToken cancellationToken = default)
    {
        var result = new List<Recipe>();
        var current = (query ?? new RecipeQuery()) with { Page = 1, PageSize = RecipeQuery.MaxPageSize };
        while (true)
        {
            var page = await SearchRecipesAsync(current, cancellationToken);
            result.AddRange(page.Items);
            if (!page.HasMore || page.Items.Count == 0)
            {
                return result;
            }

            current = current with { Page = current.Page + 1 };
        }
    }

    /// <summary><c>GET /recipes/{id}</c>. Null if the recipe does not exist.</summary>
    public async Task<Recipe?> GetRecipeAsync(int id, CancellationToken cancellationToken = default)
    {
        var dto = await GetOrNullAsync<RecipeDto>($"recipes/{id}", cancellationToken);
        return dto is null ? null : RecipeMapping.ToRecipe(dto);
    }

    /// <summary><c>POST /recipes</c>. A category given by name is created if missing; author defaults to "Mor".</summary>
    public async Task<Recipe> CreateRecipeAsync(RecipeDraft draft, CancellationToken cancellationToken = default)
    {
        var dto = await SendRequiredAsync<RecipeDto>(HttpMethod.Post, "recipes", RecipeMapping.ToInput(draft), cancellationToken);
        return RecipeMapping.ToRecipe(dto);
    }

    /// <summary><c>PUT /recipes/{id}</c> – replaces every field, ingredient and step. The photo is kept.</summary>
    public async Task<Recipe> UpdateRecipeAsync(int id, RecipeDraft draft, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<RecipeDto>(HttpMethod.Put, $"recipes/{id}", RecipeMapping.ToInput(draft), cancellationToken, allowEmpty: true);
        return dto is { Id: > 0 } ? RecipeMapping.ToRecipe(dto) : await GetRecipeAsync(id, cancellationToken) ?? throw NotFound();
    }

    /// <summary><c>DELETE /recipes/{id}</c> – also deletes ingredients, steps, favourites and the photo file.</summary>
    public Task DeleteRecipeAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"recipes/{id}", null, cancellationToken, allowEmpty: true);

    /// <summary>
    /// <c>PUT /recipes/{id}/image</c> – uploads or replaces the photo (jpg, jpeg, png, webp, gif; max 10 MB).
    /// Returns the recipe with its new <see cref="Recipe.ImageUrl"/>.
    /// </summary>
    public async Task<Recipe> SetRecipeImageAsync(int id, Stream image, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (RecipeImages.ContentType(fileName) is not { } contentType)
        {
            throw new RecipeApiException(RecipeApiError.Invalid, "Billedet skal være jpg, png, webp eller gif.");
        }

        if (image.CanSeek && image.Length - image.Position > RecipeImages.MaxBytes)
        {
            throw new RecipeApiException(RecipeApiError.Invalid, "Billedet må højst fylde 10 MB.");
        }

        var file = new StreamContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, "file", Path.GetFileName(fileName) } };

        var dto = await SendAsync<RecipeDto>(HttpMethod.Put, $"recipes/{id}/image", form, cancellationToken, allowEmpty: true, timeoutFactor: 4);
        return dto is { Id: > 0 } ? RecipeMapping.ToRecipe(dto) : await GetRecipeAsync(id, cancellationToken) ?? throw NotFound();
    }

    /// <summary><c>DELETE /recipes/{id}/image</c>.</summary>
    public Task RemoveRecipeImageAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"recipes/{id}/image", null, cancellationToken, allowEmpty: true);

    // ------------------------------------------------------------------ categories

    /// <summary><c>GET /categories</c> – with the number of recipes in each.</summary>
    public async Task<IReadOnlyList<RecipeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var list = await SendRequiredAsync<List<CategoryDto>>(HttpMethod.Get, "categories", null, cancellationToken);
        return RecipeMapping.ToCategories(list);
    }

    /// <summary><c>GET /categories/{id}</c>. Null if it does not exist.</summary>
    public async Task<RecipeCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var dto = await GetOrNullAsync<CategoryDto>($"categories/{id}", cancellationToken);
        return dto is null ? null : RecipeMapping.ToCategory(dto);
    }

    /// <summary><c>POST /categories</c>. A name already in use gives <see cref="RecipeApiError.Conflict"/>.</summary>
    public async Task<RecipeCategory> CreateCategoryAsync(RecipeCategoryDraft draft, CancellationToken cancellationToken = default)
    {
        var dto = await SendRequiredAsync<CategoryDto>(HttpMethod.Post, "categories", RecipeMapping.ToInput(draft), cancellationToken);
        return RecipeMapping.ToCategory(dto);
    }

    /// <summary><c>PUT /categories/{id}</c> – rename and/or change icon (null icon = keep). Applies to every recipe in it.</summary>
    public async Task<RecipeCategory> UpdateCategoryAsync(int id, RecipeCategoryDraft draft, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<CategoryDto>(HttpMethod.Put, $"categories/{id}", RecipeMapping.ToInput(draft), cancellationToken, allowEmpty: true);
        return dto is { Id: > 0 } ? RecipeMapping.ToCategory(dto) : await GetCategoryAsync(id, cancellationToken) ?? throw NotFound();
    }

    /// <summary><c>DELETE /categories/{id}</c> – the recipes are kept, without a category.</summary>
    public Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"categories/{id}", null, cancellationToken, allowEmpty: true);

    // ------------------------------------------------------------------ lookups

    /// <summary><c>GET /lookups</c> – difficulties, categories, units and ingredient names in one call.</summary>
    public async Task<RecipeLookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendRequiredAsync<LookupsDto>(HttpMethod.Get, "lookups", null, cancellationToken);
        return RecipeMapping.ToLookups(dto);
    }

    // ------------------------------------------------------------------ plumbing

    internal static string ToQueryString(RecipeQuery query)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
            }
        }

        Add("search", query.Search);
        Add("category", query.Category);
        Add("categoryId", query.CategoryId?.ToString(CultureInfo.InvariantCulture));
        Add("difficulty", query.Difficulty?.Label());
        Add("maxTotalMinutes", query.MaxTotalMinutes?.ToString(CultureInfo.InvariantCulture));
        foreach (var ingredient in query.Ingredients)
        {
            Add("ingredient", ingredient);
        }

        // The recipe book stores and compares UTC.
        Add("modifiedSince", query.ModifiedSince?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
        if (query.Sort != RecipeSort.Title)
        {
            Add("sort", query.Sort.ToString());
        }

        Add("page", Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture));
        Add("pageSize", Math.Clamp(query.PageSize, 1, RecipeQuery.MaxPageSize).ToString(CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "" : "?" + string.Join('&', parts);
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await SendRequiredAsync<T>(HttpMethod.Get, path, null, cancellationToken);
        }
        catch (RecipeApiException ex) when (ex.Error == RecipeApiError.NotFound)
        {
            return null;
        }
    }

    private async Task<T> SendRequiredAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        where T : class =>
        await SendAsync<T>(method, path, body, cancellationToken)
            ?? throw RecipeApiException.Unreadable(new InvalidDataException("Empty response"));

    /// <param name="allowEmpty">The answer's body is optional (or its shape undocumented): empty or unreadable → null.</param>
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowEmpty = false, int timeoutFactor = 1)
        where T : class
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured || !settings.TryGetBaseUri(out var baseUri))
        {
            throw RecipeApiException.NotConfigured();
        }

        using var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds) * timeoutFactor);

        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = body switch
        {
            null => null,
            HttpContent content => content,
            _ => JsonContent.Create(body, body.GetType(), options: Json),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Recipe book unreachable: {Method} {Path}", method, path);
            throw RecipeApiException.Offline(ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                var error = RecipeMapping.ToException(response.StatusCode, text);
                logger.Log(error.Error is RecipeApiError.Failed or RecipeApiError.Unauthorized ? LogLevel.Warning : LogLevel.Debug,
                    "Recipe book answered {Status} to {Method} {Path}: {Body}", (int)response.StatusCode, method, path, text);
                throw error;
            }

            if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
            {
                return allowEmpty ? null : throw RecipeApiException.Unreadable(new InvalidDataException("Empty response"));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return allowEmpty ? null : throw RecipeApiException.Unreadable(new InvalidDataException("Empty response"));
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("null");
            }
            catch (JsonException ex) when (!allowEmpty)
            {
                logger.LogWarning(ex, "Unreadable answer from the recipe book to {Method} {Path}", method, path);
                throw RecipeApiException.Unreadable(ex);
            }
            catch (JsonException)
            {
                // The call succeeded; the answer just isn't the shape we hoped for – the caller fetches instead.
                return null;
            }
        }
    }

    private static RecipeApiException NotFound() => new(RecipeApiError.NotFound, "Findes ikke længere i opskriftsbogen.", HttpStatusCode.NotFound);
}

/// <summary>Photo rules from the API: formats and size.</summary>
public static class RecipeImages
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static IReadOnlyList<string> AllowedExtensions { get; } = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    /// <summary>The MIME type for an allowed file name, otherwise null.</summary>
    public static string? ContentType(string? fileName) => Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => null,
    };
}

// ---------------------------------------------------------------------- wire format (as the API sends it)

internal sealed record RecipePageDto(List<RecipeDto>? Items, int TotalCount, int Page, int PageSize, int TotalPages);

internal sealed record RecipeDto(
    int Id,
    string? Title,
    int? CategoryId,
    string? Category,
    string? CategoryIcon,
    int PrepTimeMinutes,
    int CookTimeMinutes,
    int TotalTimeMinutes,
    int Servings,
    string? Difficulty,
    string? Author,
    string? Notes,
    DateTime? LastModified,
    string? ImageUrl,
    List<IngredientDto?>? Ingredients,
    List<string?>? Steps);

internal sealed record IngredientDto(string? Amount, double? Quantity, string? Unit, string? Name);

internal sealed record CategoryDto(int Id, string? Name, string? Icon, int RecipeCount);

internal sealed record IngredientUsageDto(string? Name, int RecipeCount);

internal sealed record LookupsDto(List<string?>? Difficulties, List<CategoryDto>? Categories, List<string?>? Units, List<IngredientUsageDto>? Ingredients);

internal sealed record ProblemDto(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);

/// <summary>Translation between the API's JSON and Family Hub's model. Pure functions – unit-tested.</summary>
internal static class RecipeMapping
{
    public static RecipePage ToPage(RecipePageDto dto) => new()
    {
        Items = [.. (dto.Items ?? []).Select(ToRecipe)],
        TotalCount = dto.TotalCount,
        Page = dto.Page,
        PageSize = dto.PageSize,
        TotalPages = dto.TotalPages,
    };

    public static Recipe ToRecipe(RecipeDto dto) => new()
    {
        Id = dto.Id,
        Title = Text(dto.Title) is { Length: > 0 } title ? title : "Uden navn",
        CategoryId = dto.CategoryId,
        Category = Text(dto.Category),
        CategoryIcon = Text(dto.CategoryIcon),
        PrepTimeMinutes = Math.Max(0, dto.PrepTimeMinutes),
        CookTimeMinutes = Math.Max(0, dto.CookTimeMinutes),
        TotalTimeMinutes = Math.Max(0, dto.TotalTimeMinutes),
        Servings = Math.Max(0, dto.Servings),
        Difficulty = RecipeDifficultyExtensions.ParseDifficulty(dto.Difficulty),
        Author = Text(dto.Author),
        Notes = Text(dto.Notes),
        LastModified = Utc(dto.LastModified),
        ImageUrl = Text(dto.ImageUrl) is { Length: > 0 } url ? url : null,
        Ingredients = [.. (dto.Ingredients ?? []).OfType<IngredientDto>().Select(ToIngredient).OfType<RecipeIngredient>()],
        Steps = [.. (dto.Steps ?? []).Select(s => Text(s).Replace("\r\n", "\n")).Where(s => s.Length > 0)],
    };

    public static RecipeIngredient? ToIngredient(IngredientDto dto) => Text(dto.Name) is { Length: > 0 } name
        ? new RecipeIngredient { Amount = Text(dto.Amount), Quantity = dto.Quantity, Unit = Text(dto.Unit), Name = name }
        : null;

    public static RecipeCategory ToCategory(CategoryDto dto) => new()
    {
        Id = dto.Id,
        Name = Text(dto.Name),
        Icon = Text(dto.Icon) is { Length: > 0 } icon ? icon : RecipeCategoryDraft.DefaultIcon,
        RecipeCount = Math.Max(0, dto.RecipeCount),
    };

    public static IReadOnlyList<RecipeCategory> ToCategories(IEnumerable<CategoryDto>? list) =>
        [.. (list ?? []).Select(ToCategory).Where(c => c.Name.Length > 0)];

    public static RecipeLookups ToLookups(LookupsDto dto)
    {
        var difficulties = (dto.Difficulties ?? []).Select(RecipeDifficultyExtensions.ParseDifficulty).OfType<RecipeDifficulty>().ToList();
        return new RecipeLookups
        {
            Difficulties = difficulties.Count > 0 ? difficulties : RecipeLookups.Empty.Difficulties,
            Categories = ToCategories(dto.Categories),
            Units = [.. (dto.Units ?? []).Select(Text).Where(u => u.Length > 0)],
            Ingredients = [.. (dto.Ingredients ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Name)).Select(i => new IngredientUsage(i.Name!.Trim(), i.RecipeCount))],
        };
    }

    /// <summary>The body for POST/PUT /recipes – trimmed, with blank lines left out.</summary>
    public static object ToInput(RecipeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new
        {
            title = draft.Title.Trim(),
            categoryId = draft.CategoryId,
            category = Blank(draft.Category),
            categoryIcon = Blank(draft.CategoryIcon),
            prepTimeMinutes = draft.PrepTimeMinutes,
            cookTimeMinutes = draft.CookTimeMinutes,
            servings = draft.Servings,
            difficulty = draft.Difficulty?.Label(),
            author = Blank(draft.Author),
            notes = draft.Notes?.Trim(),
            ingredients = draft.Ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new { amount = Blank(i.Amount), unit = Blank(i.Unit), name = i.Name.Trim() })
                .ToList(),
            steps = draft.Steps.Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
        };
    }

    public static object ToInput(RecipeCategoryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new { name = draft.Name.Trim(), icon = Blank(draft.Icon) };
    }

    /// <summary>A failed answer as an exception with a short Danish message (the API's own detail when it has one).</summary>
    public static RecipeApiException ToException(HttpStatusCode status, string? body)
    {
        ProblemDto? problem = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                problem = JsonSerializer.Deserialize<ProblemDto>(body, RecipeApiClient.Json);
            }
            catch (JsonException)
            {
                // Not ProblemDetails (e.g. an HTML page from a proxy) – the status code decides.
            }
        }

        var detail = Text(problem?.Detail) is { Length: > 0 } d ? d : null;
        var fieldErrors = problem?.Errors;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new(RecipeApiError.Unauthorized, "Opskriftsbogen afviste nøglen.", status),
            HttpStatusCode.NotFound =>
                new(RecipeApiError.NotFound, "Findes ikke længere i opskriftsbogen.", status),
            HttpStatusCode.Conflict =>
                new(RecipeApiError.Conflict, detail ?? "Findes allerede i opskriftsbogen.", status),
            HttpStatusCode.RequestEntityTooLarge =>
                new(RecipeApiError.Invalid, "Billedet må højst fylde 10 MB.", status),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                new(RecipeApiError.Invalid, detail ?? "Opskriftsbogen afviste oplysningerne.", status, fieldErrors),
            _ => new(RecipeApiError.Failed, "Opskriftsbogen svarede med en fejl.", status),
        };
    }

    /// <summary>The API sends UTC without an offset ("2026-09-20T16:38:45.89").</summary>
    public static DateTimeOffset Utc(DateTime? value) => value switch
    {
        null => DateTimeOffset.MinValue,
        { Kind: DateTimeKind.Unspecified } v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)),
        { } v => new DateTimeOffset(v.ToUniversalTime()),
    };

    private static string Text(string? value) => value?.Trim() ?? "";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
