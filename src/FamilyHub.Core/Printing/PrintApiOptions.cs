namespace FamilyHub.Core.Printing;

/// <summary>
/// Where the family's print server (Print API) lives – the <c>FamilyHub:Printing</c> section.
/// The API key is a secret: <c>.env</c> on the server (<c>PRINT_API_KEY</c>), <c>dotnet user-secrets</c> during development.
/// Use a standard key (<c>ak_…</c>, may only print) – never the print server's master key. See docs/printer.md.
/// </summary>
public sealed class PrintApiOptions
{
    public const string SectionName = "FamilyHub:Printing";

    /// <summary>The Print API's root, e.g. <c>http://homelab.local:8080</c>. Empty = printing not set up.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The key sent as <c>x-api-key</c>. Empty = not set up yet.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The printer to use. Empty = the only printer on the print server.</summary>
    public int? PrinterId { get; set; }

    /// <summary>Seconds before a single call gives up (sending a document gets three times as long).</summary>
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && TryGetBaseUri(out _);

    /// <summary>The base URL with a trailing slash, so relative paths ("Print/Submit") are appended, not replaced.</summary>
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
