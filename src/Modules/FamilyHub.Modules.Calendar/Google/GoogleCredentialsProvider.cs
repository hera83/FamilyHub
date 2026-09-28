using System.Text.Json;
using FamilyHub.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.Calendar.Google;

/// <summary>The OAuth client Family Hub signs in with (a "Desktop app" client from Google Cloud).</summary>
public sealed record GoogleClientCredentials(string ClientId, string ClientSecret);

/// <summary>
/// Finds the Google OAuth client. Checked on every use, so dropping the file in place works without a restart.
/// <list type="number">
/// <item>Configuration <c>FamilyHub:Calendar:Google:ClientId</c> / <c>ClientSecret</c> (e.g. <c>dotnet user-secrets</c>).</item>
/// <item>The JSON file downloaded from Google Cloud: <c>FamilyHub:Calendar:Google:KeyFile</c> if set
/// (relative to the content root – development points it at <c>secrets/</c>), otherwise <c>kalender/google-client.json</c> in the data folder.</item>
/// </list>
/// Secrets never live in appsettings.json in git.
/// </summary>
public sealed class GoogleCredentialsProvider(IConfiguration configuration, IAppDataPaths paths, IHostEnvironment environment, ILogger<GoogleCredentialsProvider> logger)
{
    public const string RelativeFilePath = "kalender/google-client.json";
    public const string ConfigurationSection = "FamilyHub:Calendar:Google";

    private readonly Lock gate = new();
    private DateTime cachedWriteTime;
    private GoogleClientCredentials? cachedFromFile;

    /// <summary>Full path of the credentials file – shown on the settings page while it is missing.</summary>
    public string FilePath => configuration.GetSection(ConfigurationSection)["KeyFile"] is { Length: > 0 } keyFile
        ? Path.GetFullPath(keyFile, environment.ContentRootPath)
        : paths.GetFilePath(RelativeFilePath);

    public GoogleClientCredentials? Get()
    {
        var section = configuration.GetSection(ConfigurationSection);
        if (section["ClientId"] is { Length: > 0 } id && section["ClientSecret"] is { Length: > 0 } secret)
        {
            return new GoogleClientCredentials(id, secret);
        }

        return ReadFile();
    }

    private GoogleClientCredentials? ReadFile()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists)
        {
            return null;
        }

        lock (gate)
        {
            if (file.LastWriteTimeUtc == cachedWriteTime)
            {
                return cachedFromFile;
            }

            cachedWriteTime = file.LastWriteTimeUtc;
            try
            {
                cachedFromFile = Parse(File.ReadAllText(file.FullName));
                if (cachedFromFile is null)
                {
                    logger.LogWarning("{Path} does not contain client_id and client_secret", file.FullName);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Could not read Google credentials from {Path}", file.FullName);
                cachedFromFile = null;
            }

            return cachedFromFile;
        }
    }

    /// <summary>
    /// Accepts the file exactly as Google Cloud downloads it (<c>{"installed": {...}}</c> or <c>{"web": {...}}</c>)
    /// or a flat <c>{"client_id": "...", "client_secret": "..."}</c>.
    /// </summary>
    internal static GoogleClientCredentials? Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        foreach (var wrapper in new[] { "installed", "web" })
        {
            if (root.TryGetProperty(wrapper, out var inner) && inner.ValueKind == JsonValueKind.Object)
            {
                root = inner;
                break;
            }
        }

        var id = root.TryGetProperty("client_id", out var idElement) ? idElement.GetString() : null;
        var secret = root.TryGetProperty("client_secret", out var secretElement) ? secretElement.GetString() : null;
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : new GoogleClientCredentials(id.Trim(), secret.Trim());
    }
}
