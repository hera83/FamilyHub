using System.Net;

namespace FamilyHub.Core.Printing;

/// <summary>What went wrong when printing – decides what the UI says.</summary>
public enum PrintApiError
{
    /// <summary>No address or API key in the configuration.</summary>
    NotConfigured,

    /// <summary>The print server could not be reached – it is down, or the address is wrong.</summary>
    Offline,

    /// <summary>The API key was rejected (401/403).</summary>
    Unauthorized,

    /// <summary>No printer chosen and the print server has none, or several (see <see cref="PrintApiOptions.PrinterId"/>).</summary>
    NoPrinter,

    /// <summary>The printer or print job does not exist (any more) (404).</summary>
    NotFound,

    /// <summary>The document or the options were rejected (400/422), locally or by the print server.</summary>
    Invalid,

    /// <summary>Not possible right now: the printer is switched off in the print server, or the job is already done (409).</summary>
    Conflict,

    /// <summary>The print server is up, but the printer does not answer (502/503/504).</summary>
    PrinterUnavailable,

    /// <summary>Anything else (5xx, unreadable answer). Details are in the log.</summary>
    Failed,
}

/// <summary>
/// A failed print call. <see cref="Exception.Message"/> is short, Danish and fit for a toast or an InfoBox.
/// </summary>
public sealed class PrintApiException(PrintApiError error, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public PrintApiError Error { get; } = error;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    internal static PrintApiException NotConfigured() =>
        new(PrintApiError.NotConfigured, "Printeren er ikke sat op endnu.");

    internal static PrintApiException Offline(Exception inner) =>
        new(PrintApiError.Offline, "Printserveren kan ikke nås lige nu.", innerException: inner);

    internal static PrintApiException Unreadable(Exception inner) =>
        new(PrintApiError.Failed, "Printserveren svarede med noget uventet.", innerException: inner);

    internal static PrintApiException Invalid(string message) => new(PrintApiError.Invalid, message);
}
