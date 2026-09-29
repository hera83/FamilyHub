using System.Net;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>What went wrong when talking to the recipe book – decides what the UI says.</summary>
public enum RecipeApiError
{
    /// <summary>No API key (or no valid address) in the configuration.</summary>
    NotConfigured,

    /// <summary>The recipe book could not be reached – probably no internet, or it is down.</summary>
    Offline,

    /// <summary>The API key was rejected (401).</summary>
    Unauthorized,

    /// <summary>The recipe, category or image does not exist (any more) (404).</summary>
    NotFound,

    /// <summary>The data was rejected (400) – see <see cref="RecipeApiException.FieldErrors"/>.</summary>
    Invalid,

    /// <summary>Clashes with something that exists, e.g. a category name already in use (409).</summary>
    Conflict,

    /// <summary>Anything else (5xx, unreadable answer). Details are in the log.</summary>
    Failed,
}

/// <summary>
/// A failed call to the recipe book. <see cref="Exception.Message"/> is short, Danish and fit for a toast or an InfoBox.
/// </summary>
public sealed class RecipeApiException : Exception
{
    public RecipeApiException(RecipeApiError error, string message, HttpStatusCode? statusCode = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
        FieldErrors = fieldErrors ?? new Dictionary<string, string[]>();
    }

    public RecipeApiError Error { get; }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>The API's validation errors by field ("Title", "Ingredients[0].Name" …). Messages are as the API wrote them.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; }

    /// <summary>The first error for a field (case-insensitive), or null – for a field's <c>Error</c> parameter.</summary>
    public string? FieldError(string field) =>
        FieldErrors.FirstOrDefault(e => string.Equals(e.Key, field, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    internal static RecipeApiException NotConfigured() =>
        new(RecipeApiError.NotConfigured, "Opskriftsbogen er ikke sat op endnu.");

    internal static RecipeApiException Offline(Exception inner) =>
        new(RecipeApiError.Offline, "Opskriftsbogen kan ikke nås lige nu.", innerException: inner);

    internal static RecipeApiException Unreadable(Exception inner) =>
        new(RecipeApiError.Failed, "Opskriftsbogen svarede med noget uventet.", innerException: inner);
}
