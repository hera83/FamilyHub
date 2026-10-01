using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Printing;

/// <summary>
/// The Print API calls a standard key may use: <c>GET /Health</c> and <c>/Print/*</c>, one method per endpoint.
/// The printer administration (<c>/Hp</c>, <c>/Keys</c>, <c>/Log</c>) needs the master key and is left to Swagger UI.
/// Throws <see cref="PrintApiException"/> with a Danish message; "get one" returns null for 404.
/// Screens normally use <see cref="PrintService"/>. See docs/printer.md.
/// </summary>
public sealed class PrintApiClient(IHttpClientFactory httpClients, IOptionsMonitor<PrintApiOptions> options, ILogger<PrintApiClient> logger)
{
    public const string HttpClientName = "FamilyHub.Printing";
    public const string ApiKeyHeader = "x-api-key";

    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConfigured => options.CurrentValue.IsConfigured;

    /// <summary>
    /// <c>GET /Health</c> – every active printer and whether it answers right now (takes up to ~3 s).
    /// The print server answers 503 when no printer is online; that is still a normal answer here.
    /// </summary>
    public async Task<PrintServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<HealthDto>(HttpMethod.Get, "Health", null, PrintCall.Health, cancellationToken);
        return PrintMapping.ToStatus(dto);
    }

    /// <summary><c>POST /Print/Submit</c> – queues the PDF on the print server. Answers at once; the job is printed in the background.</summary>
    public async Task<PrintJob> SubmitAsync(int printerId, ReadOnlyMemory<byte> pdf, PrintOptions printOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printOptions);
        var body = new SubmitDto(printerId, Convert.ToBase64String(pdf.Span), printOptions.Copies, printOptions.Color, printOptions.Duplex,
            string.IsNullOrWhiteSpace(printOptions.Pages) ? null : printOptions.Pages.Trim());
        var dto = await SendAsync<PrintJobDto>(HttpMethod.Post, "Print/Submit", body, PrintCall.Submit, cancellationToken, timeoutFactor: 3);
        return PrintMapping.ToJob(dto);
    }

    /// <summary><c>GET /Print/GetStatus/{id}</c>. Null if the job does not exist (or was sent with another key).</summary>
    public async Task<PrintJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await SendAsync<PrintJobDto>(HttpMethod.Get, $"Print/GetStatus/{id}", null, PrintCall.Job, cancellationToken);
            return PrintMapping.ToJob(dto);
        }
        catch (PrintApiException ex) when (ex.Error == PrintApiError.NotFound)
        {
            return null;
        }
    }

    /// <summary><c>GET /Print/GetAll</c> – one page of the jobs sent with this key, newest first.</summary>
    public async Task<PrintJobPage> GetJobsAsync(PrintJobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var dto = await SendAsync<PrintJobPageDto>(HttpMethod.Get, "Print/GetAll" + ToQueryString(query), null, PrintCall.Job, cancellationToken);
        return PrintMapping.ToPage(dto);
    }

    /// <summary>
    /// <c>POST /Print/Cancel/{id}</c> – removes a queued job, or asks the printer to stop one it is printing.
    /// <see cref="PrintApiError.Conflict"/> while the job is being sent, or when it is already done.
    /// </summary>
    public async Task<PrintJob> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PrintJobDto>(HttpMethod.Post, $"Print/Cancel/{id}", null, PrintCall.Cancel, cancellationToken);
        return PrintMapping.ToJob(dto);
    }

    // ------------------------------------------------------------------ plumbing

    internal static string ToQueryString(PrintJobQuery query)
    {
        var parts = new List<string>();
        if (query.PrinterId is { } printerId)
        {
            parts.Add("PrinterId=" + printerId.ToString(CultureInfo.InvariantCulture));
        }

        if (query.Status is { } status)
        {
            parts.Add("Status=" + status);
        }

        parts.Add("Page=" + Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture));
        parts.Add("PageSize=" + Math.Clamp(query.PageSize, 1, PrintJobQuery.MaxPageSize).ToString(CultureInfo.InvariantCulture));
        return "?" + string.Join('&', parts);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, PrintCall call, CancellationToken cancellationToken, int timeoutFactor = 1)
        where T : class
    {
        var settings = options.CurrentValue;
        if (!settings.IsConfigured || !settings.TryGetBaseUri(out var baseUri))
        {
            throw PrintApiException.NotConfigured();
        }

        using var http = httpClients.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds) * timeoutFactor);

        using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Add(ApiKeyHeader, settings.ApiKey!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Print server unreachable: {Method} {Path}", method, path);
            throw PrintApiException.Offline(ex);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // The health check answers 503 with the same body when no printer is online.
            var isAnswer = response.IsSuccessStatusCode || (call == PrintCall.Health && response.StatusCode == HttpStatusCode.ServiceUnavailable);
            if (!isAnswer)
            {
                var error = PrintMapping.ToException(response.StatusCode, json, call);
                logger.Log(error.Error is PrintApiError.Failed or PrintApiError.Unauthorized ? LogLevel.Warning : LogLevel.Debug,
                    "Print server answered {Status} to {Method} {Path}: {Body}", (int)response.StatusCode, method, path, json);
                throw error;
            }

            try
            {
                return (string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Json))
                    ?? throw new JsonException("Empty response");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Unreadable answer from the print server to {Method} {Path}", method, path);
                throw PrintApiException.Unreadable(ex);
            }
        }
    }
}

/// <summary>Which call failed – a 404 or 409 means different things for a printer and a job.</summary>
internal enum PrintCall
{
    Health,
    Submit,
    Job,
    Cancel,
}

// ---------------------------------------------------------------------- wire format (as the API sends it)

internal sealed record SubmitDto(int PrinterId, string DocumentBase64, int Copies, bool Color, bool Duplex, string? Pages);

internal sealed record HealthDto(string? Status, DateTime? TimestampUtc, int PrintersOnline, int PrintersTotal, List<PrinterHealthDto>? Printers);

internal sealed record PrinterHealthDto(int Id, string? Name, bool IsOnline, string? State, List<string?>? StateReasons, bool? IsAcceptingJobs, string? Error);

internal sealed record PrintJobPageDto(List<PrintJobDto>? Items, int TotalCount, int Page, int PageSize);

internal sealed record PrintJobDto(
    Guid Id,
    int PrinterId,
    string? PrinterName,
    int PageCount,
    string? Pages,
    int PagesToPrint,
    int Copies,
    bool Color,
    bool Duplex,
    string? Status,
    string? StatusMessage,
    bool IsFinished,
    int Attempts,
    DateTime? NextAttemptAt,
    string? PrinterJobState,
    List<string?>? PrinterJobStateReasons,
    int? PrinterImpressionsCompleted,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    DateTime? FinishedAt);

internal sealed record ProblemDto(string? Title, string? Detail, int? Status);

/// <summary>Translation between the Print API's JSON and Family Hub's model. Pure functions – unit-tested.</summary>
internal static class PrintMapping
{
    public static PrintServerStatus ToStatus(HealthDto dto)
    {
        var printers = (dto.Printers ?? []).Select(ToPrinter).ToList();
        return new PrintServerStatus
        {
            IsHealthy = printers.Any(p => p.IsOnline),
            Printers = printers,
            CheckedAt = dto.TimestampUtc is null ? DateTimeOffset.UtcNow : Utc(dto.TimestampUtc),
        };
    }

    public static PrinterInfo ToPrinter(PrinterHealthDto dto) => new()
    {
        Id = dto.Id,
        Name = Text(dto.Name) is { Length: > 0 } name ? name : $"Printer {dto.Id}",
        IsOnline = dto.IsOnline,
        State = Blank(dto.State),
        StateReasons = List(dto.StateReasons),
        IsAcceptingJobs = dto.IsAcceptingJobs,
        Error = Blank(dto.Error),
    };

    public static PrintJob ToJob(PrintJobDto dto)
    {
        // An unknown status (a newer print server) is treated as "still going" unless the server says it is done.
        var status = Enum.TryParse<PrintJobStatus>(dto.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : dto.IsFinished ? PrintJobStatus.Completed : PrintJobStatus.Queued;
        return new PrintJob
        {
            Id = dto.Id,
            PrinterId = dto.PrinterId,
            PrinterName = Text(dto.PrinterName),
            Status = status,
            IsFinished = dto.IsFinished || status.IsFinished(),
            StatusMessage = Blank(dto.StatusMessage),
            PageCount = Math.Max(0, dto.PageCount),
            PagesToPrint = Math.Max(0, dto.PagesToPrint),
            Pages = Blank(dto.Pages),
            Copies = Math.Max(1, dto.Copies),
            Color = dto.Color,
            Duplex = dto.Duplex,
            Attempts = Math.Max(0, dto.Attempts),
            NextAttemptAt = dto.NextAttemptAt is null ? null : Utc(dto.NextAttemptAt),
            PrinterJobState = Blank(dto.PrinterJobState),
            PrinterJobStateReasons = List(dto.PrinterJobStateReasons),
            ImpressionsCompleted = dto.PrinterImpressionsCompleted,
            CreatedAt = Utc(dto.CreatedAt),
            UpdatedAt = Utc(dto.UpdatedAt),
            FinishedAt = dto.FinishedAt is null ? null : Utc(dto.FinishedAt),
        };
    }

    public static PrintJobPage ToPage(PrintJobPageDto dto) => new()
    {
        Items = [.. (dto.Items ?? []).Select(ToJob)],
        TotalCount = Math.Max(0, dto.TotalCount),
        Page = Math.Max(1, dto.Page),
        PageSize = Math.Max(0, dto.PageSize),
    };

    /// <summary>A failed answer as an exception with a short Danish message. The API's own detail is English, so it only goes to the log.</summary>
    public static PrintApiException ToException(HttpStatusCode status, string? body, PrintCall call)
    {
        var detail = Detail(body);
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new(PrintApiError.Unauthorized, "Printserveren afviste nøglen.", status),
            HttpStatusCode.NotFound when call == PrintCall.Submit =>
                new(PrintApiError.NotFound, "Printeren findes ikke på printserveren.", status),
            HttpStatusCode.NotFound =>
                new(PrintApiError.NotFound, "Udskriften findes ikke længere.", status),
            HttpStatusCode.Conflict when call == PrintCall.Submit =>
                new(PrintApiError.Conflict, "Printeren er slået fra på printserveren.", status),
            HttpStatusCode.Conflict when detail.Contains("being sent", StringComparison.OrdinalIgnoreCase) =>
                new(PrintApiError.Conflict, "Udskriften sendes til printeren lige nu. Prøv igen om lidt.", status),
            HttpStatusCode.Conflict =>
                new(PrintApiError.Conflict, "Udskriften er allerede færdig.", status),
            HttpStatusCode.UnprocessableEntity when detail.Contains("two-sided", StringComparison.OrdinalIgnoreCase) =>
                new(PrintApiError.Invalid, "Printeren kan ikke udskrive på begge sider.", status),
            HttpStatusCode.BadRequest when detail.StartsWith("pages", StringComparison.OrdinalIgnoreCase) =>
                new(PrintApiError.Invalid, "Siderne findes ikke i dokumentet.", status),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge =>
                new(PrintApiError.Invalid, "Printeren kan ikke udskrive dokumentet.", status),
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                new(PrintApiError.PrinterUnavailable, "Printeren svarer ikke.", status),
            _ => new(PrintApiError.Failed, "Printserveren svarede med en fejl.", status),
        };
    }

    /// <summary>The print server sends UTC, with or without "Z" (EF Core reads it back without a kind).</summary>
    public static DateTimeOffset Utc(DateTime? value) => value switch
    {
        null => DateTimeOffset.MinValue,
        { Kind: DateTimeKind.Unspecified } v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)),
        { } v => new DateTimeOffset(v.ToUniversalTime()),
    };

    private static string Detail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemDto>(body, PrintApiClient.Json)?.Detail?.Trim() ?? "";
        }
        catch (JsonException)
        {
            // Not ProblemDetails (e.g. an HTML page from a proxy) – the status code decides.
            return "";
        }
    }

    private static IReadOnlyList<string> List(List<string?>? values) =>
        [.. (values ?? []).Select(Text).Where(v => v.Length > 0)];

    private static string Text(string? value) => value?.Trim() ?? "";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
