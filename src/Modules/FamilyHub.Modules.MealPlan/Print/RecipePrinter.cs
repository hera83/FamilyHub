using FamilyHub.Core.Printing;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Recipes;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Print;

/// <summary>
/// "Print" on a recipe: fetches the photo, builds the PDF (<see cref="RecipePdf"/>) off the screen's thread and sends it
/// to the family's printer through <see cref="PrintService"/>. See docs/printer.md.
/// </summary>
public sealed class RecipePrinter(PrintService printing, IHttpClientFactory httpClients, IHubClock clock, ILogger<RecipePrinter> logger)
{
    public const string HttpClientName = "FamilyHub.RecipePhotos";

    /// <summary>Waiting longer for a photo than this is not worth it – the recipe is printed without it.</summary>
    internal static readonly TimeSpan PhotoTimeout = TimeSpan.FromSeconds(10);

    public bool IsConfigured => printing.IsConfigured;

    /// <summary>
    /// Prints the recipe and returns the job at once (the printer starts a moment later). In colour when it has a photo,
    /// otherwise black and white. Throws <see cref="PrintApiException"/> with a Danish message.
    /// </summary>
    public async Task<PrintJob> PrintAsync(Recipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (!printing.IsConfigured)
        {
            throw new PrintApiException(PrintApiError.NotConfigured, "Printeren er ikke sat op endnu.");
        }

        var photo = await TryDownloadPhotoAsync(recipe, cancellationToken);
        var today = clock.Today;

        // Laying out and compressing the photo takes a moment – not on the screen's thread.
        var pdf = await Task.Run(() => RecipePdf.Create(recipe, photo, today), cancellationToken);
        logger.LogInformation("Printing recipe {RecipeId} '{Title}' ({Bytes} bytes)", recipe.Id, recipe.Title, pdf.Length);

        return await printing.PrintPdfAsync(pdf, new PrintOptions { Color = photo is not null }, cancellationToken);
    }

    private async Task<byte[]?> TryDownloadPhotoAsync(Recipe recipe, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(recipe.ImageUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            http.Timeout = PhotoTimeout;
            http.MaxResponseContentBufferSize = RecipeImages.MaxBytes;
            return await http.GetByteArrayAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The recipe is what matters – print it without the photo.
            logger.LogWarning(ex, "Could not fetch the photo for recipe {RecipeId} – printing without it", recipe.Id);
            return null;
        }
    }
}
