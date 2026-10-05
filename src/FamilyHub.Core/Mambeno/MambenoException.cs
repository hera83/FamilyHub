using System.Net;

namespace FamilyHub.Core.Mambeno;

/// <summary>What went wrong when reading the Mambeno API – decides what the UI says.</summary>
public enum MambenoError
{
    /// <summary>No address or API key in the configuration.</summary>
    NotConfigured,

    /// <summary>The API could not be reached – it is down, or the address is wrong.</summary>
    Offline,

    /// <summary>The API key was rejected (401/403).</summary>
    Unauthorized,

    /// <summary>The request was rejected (400), e.g. a filter the API does not accept.</summary>
    Invalid,

    /// <summary>Anything else (5xx, unreadable answer). Details are in the log.</summary>
    Failed,
}

/// <summary>
/// A failed call to the Mambeno API. <see cref="Exception.Message"/> is short, Danish and fit for a toast or an InfoBox.
/// A recipe or category that does not exist is not an error – those calls return <c>null</c>.
/// </summary>
public sealed class MambenoException(MambenoError error, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public MambenoError Error { get; } = error;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    internal static MambenoException NotConfigured() =>
        new(MambenoError.NotConfigured, "Mambeno er ikke sat op endnu.");

    internal static MambenoException Offline(Exception inner) =>
        new(MambenoError.Offline, "Mambeno kan ikke nås lige nu.", innerException: inner);

    internal static MambenoException Unreadable(Exception inner) =>
        new(MambenoError.Failed, "Mambeno svarede med noget uventet.", innerException: inner);
}
