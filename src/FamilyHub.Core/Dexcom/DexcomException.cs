using System.Net;

namespace FamilyHub.Core.Dexcom;

/// <summary>What went wrong when reading the glucose API – decides what the UI says.</summary>
public enum DexcomError
{
    /// <summary>No address or API key in the configuration.</summary>
    NotConfigured,

    /// <summary>The API could not be reached – it is down, or the address is wrong.</summary>
    Offline,

    /// <summary>The API key was rejected (401/403).</summary>
    Unauthorized,

    /// <summary>The API has no reading yet (404).</summary>
    NoData,

    /// <summary>The request was rejected (400), e.g. a period the API does not accept.</summary>
    Invalid,

    /// <summary>Anything else (5xx, unreadable answer). Details are in the log.</summary>
    Failed,
}

/// <summary>
/// A failed call to the glucose API. <see cref="Exception.Message"/> is short, Danish and fit for a toast or an InfoBox.
/// </summary>
public sealed class DexcomException(DexcomError error, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public DexcomError Error { get; } = error;

    public HttpStatusCode? StatusCode { get; } = statusCode;

    internal static DexcomException NotConfigured() =>
        new(DexcomError.NotConfigured, "Blodsukker er ikke sat op endnu.");

    internal static DexcomException Offline(Exception inner) =>
        new(DexcomError.Offline, "Blodsukkermåleren kan ikke nås lige nu.", innerException: inner);

    internal static DexcomException Unreadable(Exception inner) =>
        new(DexcomError.Failed, "Blodsukkermåleren svarede med noget uventet.", innerException: inner);
}
