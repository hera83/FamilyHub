namespace FamilyHub.Core.Dexcom;

/// <summary>
/// Where the family's glucose API (Dexcom readings) lives – the <c>FamilyHub:Dexcom</c> section.
/// The API key is a secret: <c>.env</c> on the server (<c>DEXCOM_API_KEY</c>), <c>dotnet user-secrets</c> during development.
/// Use a standard key (<c>ak_…</c>) – never the API's master key. See docs/dexcom.md.
/// </summary>
public sealed class DexcomOptions
{
    public const string SectionName = "FamilyHub:Dexcom";

    /// <summary>The API's root, e.g. <c>https://dexcom.appcore.cc</c>. Empty = not set up.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The key sent as <c>x-api-key</c>. Empty = not set up yet.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Seconds before a single call gives up.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && TryGetBaseUri(out _);

    /// <summary>The base URL with a trailing slash, so relative paths ("Dexcom/Latest") are appended, not replaced.</summary>
    public bool TryGetBaseUri(out Uri baseUri)
    {
        var text = (BaseUrl ?? "").Trim();
        if (text.Length > 0 && Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            baseUri = uri;
            return true;
        }

        baseUri = null!;
        return false;
    }
}
