using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Mambeno;

/// <summary>
/// Recipes from the family's own API in front of Mambeno (see docs/mambeno.md). Read only – about 4,800 recipes.
/// Throws <see cref="MambenoException"/> with a Danish message.
/// </summary>
public interface IMambenoService
{
    /// <summary>False when the address or the key is missing – then every call throws <see cref="MambenoError.NotConfigured"/>.</summary>
    bool IsConfigured { get; }

    /// <summary><c>GET /api/v1/categories</c> – every category with its recipe count, A–Å as the API sends them.</summary>
    Task<IReadOnlyList<MambenoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/v1/categories/{id}</c> – <c>null</c> when it does not exist.</summary>
    Task<MambenoCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/v1/recipes</c> – one page of recipes matching every filter in the query.</summary>
    Task<MambenoRecipePage> SearchRecipesAsync(MambenoQuery query, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/v1/recipes/{id}</c> – <c>null</c> when it does not exist.</summary>
    Task<MambenoRecipe?> GetRecipeAsync(int id, CancellationToken cancellationToken = default);

    /// <summary><c>GET /Health</c> – whether the API is up and its data is fresh. A 503 is a normal answer here.</summary>
    Task<MambenoStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IMambenoService"/>
public sealed class MambenoService(IHttpClientFactory httpClients, IOptionsMonitor<MambenoOptions> options, ILogger<MambenoService> logger) : IMambenoService
{
    public const string HttpClientName = "FamilyHub.Mambeno";
    public const string ApiKeyHeader = "x-api-key";

    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    public async Task<IReadOnlyList<MambenoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<List<CategoryDto?>>("api/v1/categories", MambenoCall.Read, cancellationToken);
        return MambenoMapping.ToCategories(dto!);
    }

    public async Task<MambenoCategory?> GetCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<CategoryDto>($"api/v1/categories/{id}", MambenoCall.Lookup, cancellationToken);
        return dto is null ? null : MambenoMapping.TryToCategory(dto) ?? throw MambenoException.Unreadable(new JsonException("Category without id or name"));
    }

    public async Task<MambenoRecipePage> SearchRecipesAsync(MambenoQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var dto = await SendAsync<RecipePageDto>("api/v1/recipes" + MambenoMapping.ToQueryString(query), MambenoCall.Read, cancellationToken);
        return MambenoMapping.ToPage(dto!);
    }

    public async Task<MambenoRecipe?> GetRecipeAsync(int id, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<RecipeDto>($"api/v1/recipes/{id}", MambenoCall.Lookup, cancellationToken);
        return dto is null ? null : MambenoMapping.TryToRecipe(dto) ?? throw MambenoException.Unreadable(new JsonException("Recipe without id or title"));
    }

    public async Task<MambenoStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<HealthDto>("Health", MambenoCall.Health, cancellationToken);
        return MambenoMapping.ToStatus(dto!);
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Returns <c>null</c> only for a <see cref="MambenoCall.Lookup"/> that answers 404.</summary>
    private async Task<T?> SendAsync<T>(string path, MambenoCall call, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured || !settings.TryGetBaseUri(out var baseUri))
        {
            throw MambenoException.NotConfigured();
        }

        using var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Mambeno API unreachable: GET {Path}", path);
            throw MambenoException.Offline(ex);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (call == MambenoCall.Lookup && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            // The health check answers 503 with the same body when the data is stale.
            var isAnswer = response.IsSuccessStatusCode || (call == MambenoCall.Health && response.StatusCode == HttpStatusCode.ServiceUnavailable);
            if (!isAnswer)
            {
                var error = MambenoMapping.ToException(response.StatusCode);
                logger.Log(error.Error is MambenoError.Failed or MambenoError.Unauthorized ? LogLevel.Warning : LogLevel.Debug,
                    "Mambeno API answered {Status} to GET {Path}: {Body}", (int)response.StatusCode, path, json);
                throw error;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("Empty response");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Unreadable answer from the Mambeno API to GET {Path}", path);
                throw MambenoException.Unreadable(ex);
            }
        }
    }
}

internal enum MambenoCall
{
    /// <summary>Must answer 2xx.</summary>
    Read,

    /// <summary>A single item: 404 means "does not exist".</summary>
    Lookup,

    /// <summary>The health check: 503 is a normal answer.</summary>
    Health,
}

// ---------------------------------------------------------------------- wire format (as the API sends it)

internal sealed record CategoryDto(int? Id, string? Name, string? Icon, int? RecipeCount, int? ParentId);

internal sealed record CategoryRefDto(int? Id, string? Name, string? Icon);

internal sealed record IngredientDto(string? Amount, double? Quantity, string? Unit, string? Name, string? Group, int? LinkedRecipeId);

internal sealed record RecipeDto(
    int? Id,
    string? Title,
    string? Description,
    int? CategoryId,
    string? Category,
    List<CategoryRefDto?>? Categories,
    int? PrepTimeMinutes,
    int? CookTimeMinutes,
    int? TotalTimeMinutes,
    int? Servings,
    string? Author,
    string? Notes,
    DateTimeOffset? LastModified,
    string? ImageUrl,
    string? SourceUrl,
    List<IngredientDto?>? Ingredients,
    List<string?>? Steps);

internal sealed record RecipePageDto(List<RecipeDto?>? Items, int? TotalCount, int? Page, int? PageSize, int? TotalPages);

internal sealed record HealthDto(string? Status, DateTimeOffset? TimestampUtc, DateTimeOffset? DataLastUpdatedUtc, string? Message);

/// <summary>Translation between the API's JSON and Family Hub's model. Pure functions – unit-tested.</summary>
internal static class MambenoMapping
{
    public static string ToQueryString(MambenoQuery query)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
            }
        }

        Add("search", query.Search);
        Add("category", query.Category);
        Add("categoryId", query.CategoryId?.ToString(CultureInfo.InvariantCulture));
        Add("maxTotalMinutes", query.MaxTotalMinutes is > 0 and var minutes ? minutes.ToString(CultureInfo.InvariantCulture) : null);
        foreach (var ingredient in query.Ingredients)
        {
            Add("ingredient", ingredient);
        }

        // UTC with "Z", so the "+" of an offset never needs escaping.
        Add("modifiedSince", query.ModifiedSince?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        if (query.Sort != MambenoSort.Title)
        {
            Add("sort", query.Sort.ToString());
        }

        Add("page", Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture));
        Add("pageSize", Math.Clamp(query.PageSize, 1, MambenoQuery.MaxPageSize).ToString(CultureInfo.InvariantCulture));
        return "?" + string.Join('&', parts);
    }

    public static IReadOnlyList<MambenoCategory> ToCategories(List<CategoryDto?> dto) =>
        [.. dto.Select(TryToCategory).OfType<MambenoCategory>()];

    public static MambenoCategory? TryToCategory(CategoryDto? dto) =>
        dto is { Id: { } id, Name: { } name } && !string.IsNullOrWhiteSpace(name)
            ? new MambenoCategory { Id = id, Name = name.Trim(), RecipeCount = Math.Max(0, dto.RecipeCount ?? 0), ParentId = dto.ParentId }
            : null;

    public static MambenoRecipePage ToPage(RecipePageDto dto)
    {
        // A broken recipe is skipped rather than failing the whole page.
        var items = (dto.Items ?? []).Select(TryToRecipe).OfType<MambenoRecipe>().ToList();
        var pageSize = dto.PageSize is > 0 and var size ? size : items.Count;
        var totalCount = Math.Max(dto.TotalCount ?? items.Count, items.Count);
        return new MambenoRecipePage
        {
            Items = items,
            TotalCount = totalCount,
            Page = Math.Max(1, dto.Page ?? 1),
            PageSize = pageSize,
            TotalPages = dto.TotalPages ?? (pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0),
        };
    }

    public static MambenoRecipe? TryToRecipe(RecipeDto? dto)
    {
        if (dto is not { Id: { } id, Title: { } title } || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new MambenoRecipe
        {
            Id = id,
            Title = title.Trim(),
            Description = Text(dto.Description),
            CategoryId = dto.CategoryId,
            Category = Text(dto.Category),
            Categories = [.. (dto.Categories ?? [])
                .Where(c => c is { Id: not null } && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new MambenoCategoryRef(c!.Id!.Value, c.Name!.Trim()))],
            PrepTimeMinutes = Math.Max(0, dto.PrepTimeMinutes ?? 0),
            CookTimeMinutes = Math.Max(0, dto.CookTimeMinutes ?? 0),
            TotalTimeMinutes = Math.Max(0, dto.TotalTimeMinutes ?? 0),
            Servings = Math.Max(0, dto.Servings ?? 0),
            Author = Text(dto.Author),
            Notes = Text(dto.Notes),
            LastModified = dto.LastModified ?? DateTimeOffset.MinValue,
            ImageUrl = OrNull(dto.ImageUrl),
            SourceUrl = OrNull(dto.SourceUrl),
            Ingredients = [.. (dto.Ingredients ?? [])
                .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new MambenoIngredient
                {
                    Amount = Text(i!.Amount),
                    Quantity = i.Quantity is { } q && double.IsFinite(q) ? q : null,
                    Unit = Text(i.Unit),
                    Name = i.Name!.Trim(),
                    Group = OrNull(i.Group),
                    LinkedRecipeId = i.LinkedRecipeId,
                })],
            Steps = [.. (dto.Steps ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())],
        };
    }

    public static MambenoStatus ToStatus(HealthDto dto)
    {
        var status = string.IsNullOrWhiteSpace(dto.Status) ? "Unknown" : dto.Status.Trim();
        return new MambenoStatus
        {
            IsHealthy = status.Equals("Healthy", StringComparison.OrdinalIgnoreCase),
            Status = status,
            CheckedAt = dto.TimestampUtc ?? DateTimeOffset.UtcNow,
            DataLastUpdatedAt = dto.DataLastUpdatedUtc,
            Message = OrNull(dto.Message),
        };
    }

    /// <summary>A failed answer as an exception with a short Danish message. The API's own detail is English, so it only goes to the log.</summary>
    public static MambenoException ToException(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(MambenoError.Unauthorized, "Mambeno afviste nøglen.", status),
        HttpStatusCode.BadRequest =>
            new(MambenoError.Invalid, "Mambeno kunne ikke bruge søgningen.", status),
        _ => new(MambenoError.Failed, "Mambeno svarede med en fejl.", status),
    };

    private static string Text(string? value) => value?.Trim() ?? "";

    private static string? OrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
