using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FamilyHub.Modules.Calendar.Model;

namespace FamilyHub.Modules.Calendar.Google;

/// <summary>Google rejected the access token (expired early or revoked) – refresh and try once more.</summary>
public sealed class GoogleUnauthorizedException() : Exception("Google afviste adgangen.");

/// <summary>The few Google Calendar API v3 calls Family Hub needs. Plain HTTP + JSON, no SDK.</summary>
public sealed class GoogleCalendarApi(IHttpClientFactory httpClients)
{
    internal const string BaseUrl = "https://www.googleapis.com/calendar/v3/";

    private const string CalendarFields = "items(id,summary,summaryOverride,accessRole,primary,selected,hidden),nextPageToken";
    private const string EventFields = "items(id,status,summary,location,description,start,end,eventType),nextPageToken";

    public async Task<IReadOnlyList<CalendarSource>> ListCalendarsAsync(string accessToken, string accountId, CancellationToken cancellationToken = default)
    {
        var result = new List<CalendarSource>();
        string? pageToken = null;
        do
        {
            var url = $"users/me/calendarList?minAccessRole=reader&showHidden=false&maxResults=250&fields={Uri.EscapeDataString(CalendarFields)}{PageParameter(pageToken)}";
            var page = await GetAsync<GoogleList<GoogleCalendarDto>>(accessToken, url, cancellationToken);
            result.AddRange((page.Items ?? []).Select(c => GoogleMapping.ToSource(c, accountId)).OfType<CalendarSource>());
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    public async Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(string accessToken, string calendarId, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        var result = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var url = $"calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&maxResults=2500"
                + $"&timeMin={Uri.EscapeDataString(GoogleMapping.Rfc3339(from))}&timeMax={Uri.EscapeDataString(GoogleMapping.Rfc3339(to))}"
                + $"&fields={Uri.EscapeDataString(EventFields)}{PageParameter(pageToken)}";
            var page = await GetAsync<GoogleList<GoogleEventDto>>(accessToken, url, cancellationToken);
            result.AddRange((page.Items ?? []).Select(e => GoogleMapping.ToEvent(e, calendarId, timeZone)).OfType<CalendarEvent>());
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    public async Task<CalendarEvent> InsertEventAsync(string accessToken, NewCalendarEvent newEvent, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(accessToken);
        var url = $"calendars/{Uri.EscapeDataString(newEvent.CalendarId)}/events?fields={Uri.EscapeDataString("id,status,summary,location,description,start,end,eventType")}";
        using var response = await http.PostAsJsonAsync(url, GoogleMapping.ToInsertBody(newEvent, timeZone), cancellationToken);
        ThrowOnError(response);
        var created = await response.Content.ReadFromJsonAsync<GoogleEventDto>(cancellationToken) ?? throw new HttpRequestException("Tomt svar fra Google.");
        return GoogleMapping.ToEvent(created, newEvent.CalendarId, timeZone) ?? throw new HttpRequestException("Google oprettede ikke aftalen.");
    }

    private async Task<T> GetAsync<T>(string accessToken, string url, CancellationToken cancellationToken)
    {
        using var http = CreateClient(accessToken);
        using var response = await http.GetAsync(url, cancellationToken);
        ThrowOnError(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new HttpRequestException("Tomt svar fra Google.");
    }

    private HttpClient CreateClient(string accessToken)
    {
        var http = httpClients.CreateClient(GoogleOAuthClient.HttpClientName);
        http.BaseAddress = new Uri(BaseUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    private static void ThrowOnError(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleUnauthorizedException();
        }

        response.EnsureSuccessStatusCode();
    }

    private static string PageParameter(string? pageToken) =>
        string.IsNullOrEmpty(pageToken) ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}";
}

internal sealed record GoogleList<T>(List<T>? Items, string? NextPageToken);

internal sealed record GoogleCalendarDto(string? Id, string? Summary, string? SummaryOverride, string? AccessRole, bool Primary, bool Selected, bool Hidden);

internal sealed record GoogleEventDto(string? Id, string? Status, string? Summary, string? Location, string? Description, GoogleEventTime? Start, GoogleEventTime? End, string? EventType);

internal sealed record GoogleEventTime(string? Date, DateTimeOffset? DateTime, string? TimeZone = null);

/// <summary>Translation between Google's JSON and Family Hub's model. Pure functions – unit-tested.</summary>
internal static partial class GoogleMapping
{
    public const int MaxDescriptionLength = 2000;

    public static CalendarSource? ToSource(GoogleCalendarDto dto, string accountId)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            return null;
        }

        var name = FirstText(dto.SummaryOverride, dto.Summary) ?? dto.Id;
        return new CalendarSource
        {
            Id = dto.Id,
            AccountId = accountId,
            Name = name,
            CanWrite = dto.AccessRole is "owner" or "writer",
            IsPrimary = dto.Primary,
            SelectedInGoogle = dto.Selected && !dto.Hidden,
        };
    }

    public static CalendarEvent? ToEvent(GoogleEventDto dto, string calendarId, TimeZoneInfo timeZone)
    {
        // Cancelled occurrences and Google's "working location" markers are not appointments.
        if (string.IsNullOrEmpty(dto.Id) || dto.Status == "cancelled" || dto.EventType == "workingLocation"
            || dto.Start is null || dto.End is null)
        {
            return null;
        }

        DateTimeOffset start, end;
        bool allDay;
        if (dto.Start.Date is { } startText && TryParseDate(startText, out var startDate))
        {
            allDay = true;
            var endDate = dto.End.Date is { } endText && TryParseDate(endText, out var parsedEnd) && parsedEnd > startDate ? parsedEnd : startDate.AddDays(1);
            start = LocalMidnight(startDate, timeZone);
            end = LocalMidnight(endDate, timeZone);
        }
        else if (dto.Start.DateTime is { } startTime && dto.End.DateTime is { } endTime)
        {
            allDay = false;
            start = TimeZoneInfo.ConvertTime(startTime, timeZone);
            end = TimeZoneInfo.ConvertTime(endTime < startTime ? startTime : endTime, timeZone);
        }
        else
        {
            return null;
        }

        return new CalendarEvent
        {
            Id = dto.Id,
            CalendarId = calendarId,
            // Calendars shared as "free/busy only" come without a title.
            Title = Truncate(FirstText(dto.Summary) ?? "Optaget", CalendarEvent.MaxTitleLength),
            Start = start,
            End = end,
            IsAllDay = allDay,
            Location = FirstText(dto.Location),
            Description = PlainText(dto.Description),
        };
    }

    public static object ToInsertBody(NewCalendarEvent newEvent, TimeZoneInfo timeZone)
    {
        var title = Truncate(newEvent.Title.Trim(), CalendarEvent.MaxTitleLength);
        if (newEvent.IsAllDay)
        {
            var days = Math.Clamp(newEvent.Days, 1, 366);
            return new
            {
                summary = title,
                start = new { date = newEvent.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                end = new { date = newEvent.Date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            };
        }

        var local = newEvent.Date.ToDateTime(newEvent.StartTime);
        var start = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        var end = TimeZoneInfo.ConvertTime(start + newEvent.Duration, timeZone);
        return new
        {
            summary = title,
            start = new { dateTime = Rfc3339(start) },
            end = new { dateTime = Rfc3339(end) },
        };
    }

    public static string Rfc3339(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    public static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    /// <summary>Google descriptions may contain simple HTML. Shown as plain text with line breaks.</summary>
    public static string? PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreakTags().Replace(html, "\n");
        text = Tags().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = ManyBlankLines().Replace(text.Replace("\r\n", "\n"), "\n\n").Trim();
        return text.Length == 0 ? null : Truncate(text, MaxDescriptionLength);
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string? FirstText(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";

    [GeneratedRegex(@"<\s*(br|/p|/div|/li)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n\s*\n(\s*\n)+")]
    private static partial Regex ManyBlankLines();
}
