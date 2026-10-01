using System.Globalization;

namespace FamilyHub.Core.Printing;

/// <summary>
/// The print server's rules, checked before sending – so the answer is Danish and nothing large is sent in vain.
/// Pure functions – unit-tested.
/// </summary>
public static class PrintValidation
{
    /// <summary>The print server accepts requests up to 100 MB; base64 makes the PDF a third larger.</summary>
    public const long MaxDocumentBytes = 75L * 1024 * 1024;

    /// <summary>The first problem with the document or the options, in Danish – or null if it can be sent.</summary>
    public static string? Validate(ReadOnlySpan<byte> pdf, PrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (pdf.IsEmpty)
        {
            return "Dokumentet er tomt.";
        }

        // Every PDF starts with "%PDF-" (the spec allows it within the first 1024 bytes).
        if (pdf[..Math.Min(pdf.Length, 1024)].IndexOf("%PDF-"u8) < 0)
        {
            return "Kun PDF-dokumenter kan udskrives.";
        }

        if (pdf.Length > MaxDocumentBytes)
        {
            return "Dokumentet er for stort til printeren (højst 75 MB).";
        }

        if (options.Copies is < 1 or > PrintOptions.MaxCopies)
        {
            return $"Antal kopier skal være mellem 1 og {PrintOptions.MaxCopies}.";
        }

        return ValidatePages(options.Pages);
    }

    /// <summary>
    /// Checks the form of a page selection ("2-9", "5", "1,3,5-7", "3-"). Whether the pages exist is checked by the
    /// print server, which knows the document's length.
    /// </summary>
    public static string? ValidatePages(string? pages)
    {
        if (string.IsNullOrWhiteSpace(pages))
        {
            return null;
        }

        if (pages.Length > PrintOptions.MaxPagesLength)
        {
            return "Sidevalget er for langt.";
        }

        foreach (var part in pages.Split(',', StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            var fromText = dash < 0 ? part : part[..dash].Trim();
            var toText = dash < 0 ? "" : part[(dash + 1)..].Trim();
            if (!int.TryParse(fromText, NumberStyles.None, CultureInfo.InvariantCulture, out var from) || from < 1
                || (toText.Length > 0 && !int.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                return "Skriv siderne som fx 2-5, 1,3 eller 3- (side 3 og frem).";
            }

            if (toText.Length > 0 && int.Parse(toText, CultureInfo.InvariantCulture) < from)
            {
                return "Skriv den laveste side først, fx 2-5.";
            }
        }

        return null;
    }
}
