using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Modules.Calendar.Google;

/// <summary>Google refused the sign-in or the stored refresh token – the account must be connected again.</summary>
public sealed class GoogleAuthException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Result of a completed sign-in.</summary>
public sealed record GoogleSignIn(string AccountId, string Email, string RefreshToken, GoogleAccessToken AccessToken);

public sealed record GoogleAccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Google sign-in for an installed app: authorization code + PKCE with a loopback redirect
/// (http://localhost:…/kalender/google/callback). The browser that opened Family Hub on localhost
/// does the sign-in – the kitchen screen itself or a laptop through an SSH tunnel.
/// See https://developers.google.com/identity/protocols/oauth2/native-app.
/// </summary>
public sealed class GoogleOAuthClient(IHttpClientFactory httpClients, GoogleCredentialsProvider credentials, TimeProvider time)
{
    public const string HttpClientName = "FamilyHub.Google";

    /// <summary>The narrowest scopes that let us list calendars and read/add events.</summary>
    public static readonly IReadOnlyList<string> Scopes =
    [
        "openid",
        "email",
        "https://www.googleapis.com/auth/calendar.calendarlist.readonly",
        "https://www.googleapis.com/auth/calendar.events",
    ];

    internal const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    internal const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, PendingSignIn> pending = new(StringComparer.Ordinal);

    public bool IsConfigured => credentials.Get() is not null;

    /// <summary>
    /// Only a loopback address can receive Google's answer (Google allows http only for localhost),
    /// and Family Hub only listens on localhost anyway.
    /// </summary>
    public static bool CanSignInFrom(Uri baseUri) => baseUri.IsLoopback;

    /// <summary>Starts a sign-in and returns the Google page to send the browser to.</summary>
    public Uri CreateSignInUrl(Uri redirectUri)
    {
        var client = credentials.Get() ?? throw new InvalidOperationException("Google-klienten er ikke sat op.");
        RemoveExpired();

        var state = RandomToken(24);
        var verifier = RandomToken(48);
        pending[state] = new PendingSignIn(verifier, redirectUri, time.GetUtcNow() + SignInLifetime);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', Scopes),
            ["access_type"] = "offline",
            // Always ask, so Google always returns a refresh token – also when an account is connected again.
            ["prompt"] = "consent select_account",
            ["state"] = state,
            ["code_challenge"] = CodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
        };

        return new Uri($"{AuthorizationEndpoint}?{string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}");
    }

    /// <summary>Exchanges the code Google sent back for tokens.</summary>
    public async Task<GoogleSignIn> CompleteSignInAsync(string state, string code, CancellationToken cancellationToken = default)
    {
        if (!pending.TryRemove(state, out var signIn) || signIn.ExpiresAt < time.GetUtcNow())
        {
            throw new GoogleAuthException("Login-forsøget er udløbet eller ukendt.");
        }

        var client = credentials.Get() ?? throw new GoogleAuthException("Google-klienten er ikke sat op.");
        var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = signIn.RedirectUri.ToString(),
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["code_verifier"] = signIn.Verifier,
        }, cancellationToken);

        if (string.IsNullOrEmpty(response.RefreshToken))
        {
            throw new GoogleAuthException("Google sendte ingen refresh token.");
        }

        var (accountId, email) = ReadIdToken(response.IdToken);
        return new GoogleSignIn(accountId, email, response.RefreshToken, ToAccessToken(response));
    }

    /// <summary>A fresh access token. Throws <see cref="GoogleAuthException"/> if the refresh token is no longer valid.</summary>
    public async Task<GoogleAccessToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var client = credentials.Get() ?? throw new GoogleAuthException("Google-klienten er ikke sat op.");
        var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
        }, cancellationToken);
        return ToAccessToken(response);
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            // invalid_grant: revoked, expired (e.g. app still in "Testing"), password changed. invalid_client: wrong key file.
            throw new GoogleAuthException($"Google afviste login ({error}).");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new HttpRequestException("Tomt svar fra Google.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TokenError>(cancellationToken);
            return error?.Error ?? response.StatusCode.ToString();
        }
        catch (JsonException)
        {
            return response.StatusCode.ToString();
        }
    }

    private GoogleAccessToken ToAccessToken(TokenResponse response) =>
        new(response.AccessToken ?? throw new HttpRequestException("Google sendte ingen access token."),
            time.GetUtcNow().AddSeconds(Math.Max(60, response.ExpiresIn)));

    /// <summary>
    /// Reads "sub" and "email" from the id token. The token comes straight from Google's token endpoint over TLS,
    /// so its signature does not need to be checked (OpenID Connect Core 3.1.3.7).
    /// </summary>
    internal static (string AccountId, string Email) ReadIdToken(string? idToken)
    {
        var parts = idToken?.Split('.') ?? [];
        if (parts.Length < 2)
        {
            throw new GoogleAuthException("Google sendte ingen id token.");
        }

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = payload.RootElement;
        var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : null;
        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(sub))
        {
            throw new GoogleAuthException("Google-kontoen mangler et id.");
        }

        return (sub, email ?? sub);
    }

    internal static string CodeChallenge(string verifier) => Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string RandomToken(int bytes) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64UrlEncode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private void RemoveExpired()
    {
        var now = time.GetUtcNow();
        foreach (var (key, value) in pending)
        {
            if (value.ExpiresAt < now)
            {
                pending.TryRemove(key, out _);
            }
        }
    }

    private sealed record PendingSignIn(string Verifier, Uri RedirectUri, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken);

    private sealed record TokenError([property: JsonPropertyName("error")] string? Error);
}
