using FamilyHub.Core.Printing;
using FamilyHub.Core.Time;
using FamilyHub.Modules.MealPlan.Shopping;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Print;

/// <summary>
/// "Print" on the shopping list: builds the PDF (<see cref="ShoppingListPdf"/>) off the screen's thread and sends it to the
/// family's printer through <see cref="PrintService"/>, in black and white. See docs/printer.md.
/// </summary>
public sealed class ShoppingListPrinter(PrintService printing, IHubClock clock, ILogger<ShoppingListPrinter> logger)
{
    public bool IsConfigured => printing.IsConfigured;

    /// <summary>Prints the list and returns the job at once. Throws <see cref="PrintApiException"/> with a Danish message.</summary>
    public async Task<PrintJob> PrintAsync(ShoppingList list, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!printing.IsConfigured)
        {
            throw new PrintApiException(PrintApiError.NotConfigured, "Printeren er ikke sat op endnu.");
        }

        var today = clock.Today;
        var pdf = await Task.Run(() => ShoppingListPdf.Create(list, today), cancellationToken);
        logger.LogInformation("Printing the shopping list for the week of {Monday} ({Lines} lines, {Bytes} bytes)",
            list.Week, ShoppingListPdf.LinesToPrint(list).Count, pdf.Length);

        return await printing.PrintPdfAsync(pdf, new PrintOptions { Color = false }, cancellationToken);
    }
}
