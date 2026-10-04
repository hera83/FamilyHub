using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Dexcom;

/// <summary>
/// Glucose readings from the family's own API in front of Dexcom (see docs/dexcom.md).
/// Throws <see cref="DexcomException"/> with a Danish message.
/// </summary>
public interface IDexcomService
{
    /// <summary>False when the address or the key is missing – then every call throws <see cref="DexcomError.NotConfigured"/>.</summary>
    bool IsConfigured { get; }

    /// <summary><c>GET /Dexcom/Latest</c> – the newest reading. Check <see cref="GlucoseReading.Time"/>: it may be old if the sensor is out of range.</summary>
    Task<GlucoseReading> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /Dexcom/Readings</c> – every reading in the period, oldest first.</summary>
    Task<GlucoseReadings> GetReadingsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary><c>GET /Health</c> – whether the API is up and still receives readings. A 503 is a normal answer here.</summary>
    Task<DexcomStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDexcomService"/>
public sealed class DexcomService(IHttpClientFactory httpClients, IOptionsMonitor<DexcomOptions> options, ILogger<DexcomService> logger) : IDexcomService
{
    public const string HttpClientName = "FamilyHub.Dexcom";
    public const string ApiKeyHeader = "x-api-key";

    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    public async Task<GlucoseReading> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<ReadingDto>("Dexcom/Latest", DexcomCall.Latest, cancellationToken);
        return DexcomMapping.ToReading(dto);
    }

    public async Task<GlucoseReadings> GetReadingsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new DexcomException(DexcomError.Invalid, "Perioden slutter, før den begynder.");
        }

        var dto = await SendAsync<ReadingsDto>("Dexcom/Readings" + DexcomMapping.ToQueryString(from, to), DexcomCall.Readings, cancellationToken);
        return DexcomMapping.ToReadings(dto, from, to);
    }

    public async Task<DexcomStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<HealthDto>("Health", DexcomCall.Health, cancellationToken);
        return DexcomMapping.ToStatus(dto);
    }

    // ------------------------------------------------------------------ plumbing

    private async Task<T> SendAsync<T>(string path, DexcomCall call, CancellationToken cancellationToken)
        where T : class
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured || !settings.TryGetBaseUri(out var baseUri))
        {
            throw DexcomException.NotConfigured();
        }

        using var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Glucose API unreachable: GET {Path}", path);
            throw DexcomException.Offline(ex);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // The health check answers 503 with the same body when readings have stopped coming.
            var isAnswer = response.IsSuccessStatusCode || (call == DexcomCall.Health && response.StatusCode == HttpStatusCode.ServiceUnavailable);
            if (!isAnswer)
            {
                var error = DexcomMapping.ToException(response.StatusCode);
                logger.Log(error.Error is DexcomError.Failed or DexcomError.Unauthorized ? LogLevel.Warning : LogLevel.Debug,
                    "Glucose API answered {Status} to GET {Path}: {Body}", (int)response.StatusCode, path, json);
                throw error;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw DexcomMapping.ToException(HttpStatusCode.NotFound);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("Empty response");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Unreadable answer from the glucose API to GET {Path}", path);
                throw DexcomException.Unreadable(ex);
            }
        }
    }
}

internal enum DexcomCall
{
    Latest,
    Readings,
    Health,
}

// ---------------------------------------------------------------------- wire format (as the API sends it)

internal sealed record ReadingDto(DateTimeOffset? Time, double? ValueMmolL, string? Trend);

internal sealed record ReadingsDto(DateTimeOffset? From, DateTimeOffset? To, int Count, List<ReadingDto?>? Items);

internal sealed record HealthDto(string? Status, DateTimeOffset? TimestampUtc, DateTimeOffset? LatestReadingUtc, int? LatestReadingAgeSeconds, string? Reason);

/// <summary>Translation between the API's JSON and Family Hub's model. Pure functions – unit-tested.</summary>
internal static class DexcomMapping
{
    /// <summary>UTC with "Z", so the "+" of an offset never needs escaping in the query string.</summary>
    public static string ToQueryString(DateTimeOffset from, DateTimeOffset to) =>
        "?From=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) +
        "&To=" + Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    public static GlucoseReading ToReading(ReadingDto dto) =>
        TryToReading(dto) ?? throw DexcomException.Unreadable(new JsonException("Reading without time or value"));

    public static GlucoseReadings ToReadings(ReadingsDto dto, DateTimeOffset from, DateTimeOffset to) => new()
    {
        From = dto.From ?? from,
        To = dto.To ?? to,

        // A reading without time or value is skipped rather than failing the whole period.
        Items = [.. (dto.Items ?? []).Select(TryToReading).OfType<GlucoseReading>().OrderBy(r => r.Time)],
    };

    public static DexcomStatus ToStatus(HealthDto dto)
    {
        var status = string.IsNullOrWhiteSpace(dto.Status) ? "Unknown" : dto.Status.Trim();
        return new DexcomStatus
        {
            IsHealthy = status.Equals("Healthy", StringComparison.OrdinalIgnoreCase),
            Status = status,
            CheckedAt = dto.TimestampUtc ?? DateTimeOffset.UtcNow,
            LatestReadingAt = dto.LatestReadingUtc,
            LatestReadingAge = dto.LatestReadingAgeSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Max(0, seconds)) : null,
            Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
        };
    }

    public static GlucoseTrend ToTrend(string? trend) =>
        Enum.TryParse<GlucoseTrend>(trend?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(trend, out _)
            ? parsed
            : GlucoseTrend.Unknown;

    /// <summary>A failed answer as an exception with a short Danish message. The API's own detail is English, so it only goes to the log.</summary>
    public static DexcomException ToException(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new(DexcomError.Unauthorized, "Blodsukkermåleren afviste nøglen.", status),
        HttpStatusCode.NotFound =>
            new(DexcomError.NoData, "Der er ingen målinger endnu.", status),
        HttpStatusCode.BadRequest =>
            new(DexcomError.Invalid, "Blodsukkermåleren kunne ikke bruge perioden.", status),
        _ => new(DexcomError.Failed, "Blodsukkermåleren svarede med en fejl.", status),
    };

    private static GlucoseReading? TryToReading(ReadingDto? dto) =>
        dto is { Time: { } time, ValueMmolL: { } value } && double.IsFinite(value)
            ? new GlucoseReading { Time = time, MmolL = value, Trend = ToTrend(dto.Trend) }
            : null;
}
