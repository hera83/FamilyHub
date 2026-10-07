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
    private const string OneEventFields = "id,status,summary,location,description,start,end,eventType,recurringEventId,organizer(self),guestsCanModify,locked";
    private const string EventFields = $"items({OneEventFields}),nextPageToken";

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

    public async Task<CalendarEvent> InsertEventAsync(string accessToken, CalendarEventDraft newEvent, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(accessToken);
        var url = $"calendars/{Uri.EscapeDataString(newEvent.CalendarId)}/events?fields={Uri.EscapeDataString(OneEventFields)}";
        using var response = await http.PostAsJsonAsync(url, GoogleMapping.ToInsertBody(newEvent, timeZone), cancellationToken);
        return await ReadEventAsync(response, newEvent.CalendarId, timeZone, cancellationToken);
    }

    /// <summary>
    /// Changes only what is sent – location, description, guests and reminders stay as they are in Google.
    /// For an occurrence of a recurring event, only that occurrence changes.
    /// </summary>
    public async Task<CalendarEvent> PatchEventAsync(string accessToken, string calendarId, string eventId, object body, TimeZoneInfo timeZone, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(accessToken);
        var url = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}?sendUpdates=none&fields={Uri.EscapeDataString(OneEventFields)}";
        using var response = await http.PatchAsJsonAsync(url, body, cancellationToken);
        return await ReadEventAsync(response, calendarId, timeZone, cancellationToken);
    }

    /// <summary>Deletes the event (or this occurrence). Already gone counts as done.</summary>
    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient(accessToken);
        var url = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}?sendUpdates=none";
        using var response = await http.DeleteAsync(url, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return;
        }

        ThrowOnError(response);
    }

    private static async Task<CalendarEvent> ReadEventAsync(HttpResponseMessage response, string calendarId, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        ThrowOnError(response);
        var dto = await response.Content.ReadFromJsonAsync<GoogleEventDto>(cancellationToken) ?? throw new HttpRequestException("Tomt svar fra Google.");
        return GoogleMapping.ToEvent(dto, calendarId, timeZone) ?? throw new HttpRequestException("Google gemte ikke aftalen.");
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

internal sealed record GoogleEventDto(
    string? Id,
    string? Status,
    string? Summary,
    string? Location,
    string? Description,
    GoogleEventTime? Start,
    GoogleEventTime? End,
    string? EventType,
    string? RecurringEventId = null,
    GoogleOrganizer? Organizer = null,
    bool GuestsCanModify = false,
    bool Locked = false);

internal sealed record GoogleOrganizer(bool Self);

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
            RecurringEventId = FirstText(dto.RecurringEventId),
            Restriction = RestrictionOf(dto),
        };
    }

    // Birthdays come from Google Contacts and Gmail's events from the mails – Google refuses changes to them.
    private static EventRestriction RestrictionOf(GoogleEventDto dto) =>
        dto.Locked || dto.EventType is "birthday" or "fromGmail" ? EventRestriction.Locked
        : dto.Organizer is { Self: false } && !dto.GuestsCanModify ? EventRestriction.Invitation
        : EventRestriction.None;

    public static object ToInsertBody(CalendarEventDraft newEvent, TimeZoneInfo timeZone)
    {
        var title = CleanTitle(newEvent.Title);
        if (newEvent.IsAllDay)
        {
            var (startDate, endDate) = Dates(newEvent);
            return new
            {
                summary = title,
                start = new { date = DateText(startDate) },
                end = new { date = DateText(endDate) },
            };
        }

        var (start, end) = Times(newEvent, timeZone);
        return new
        {
            summary = title,
            start = new { dateTime = Rfc3339(start) },
            end = new { dateTime = Rfc3339(end) },
        };
    }

    /// <summary>
    /// The changes to an existing appointment – only what differs, so an untouched time keeps Google's own time
    /// zone. Switching between all-day and timed clears the other kind of time (null removes a field in a patch).
    /// </summary>
    public static Dictionary<string, object?> ToPatchBody(CalendarEvent original, CalendarEventDraft changes, TimeZoneInfo timeZone)
    {
        var body = new Dictionary<string, object?>();
        var title = CleanTitle(changes.Title);
        if (title != original.Title)
        {
            body["summary"] = title;
        }

        if (changes.IsAllDay)
        {
            var (startDate, endDate) = Dates(changes);
            if (!original.IsAllDay || original.StartDate != startDate || original.EndDate != endDate)
            {
                body["start"] = new Dictionary<string, object?> { ["date"] = DateText(startDate), ["dateTime"] = null };
                body["end"] = new Dictionary<string, object?> { ["date"] = DateText(endDate), ["dateTime"] = null };
            }
        }
        else
        {
            var (start, end) = Times(changes, timeZone);
            if (original.IsAllDay || original.Start != start || original.End != end)
            {
                body["start"] = new Dictionary<string, object?> { ["dateTime"] = Rfc3339(start), ["date"] = null };
                body["end"] = new Dictionary<string, object?> { ["dateTime"] = Rfc3339(end), ["date"] = null };
            }
        }

        return body;
    }

    /// <summary>Undo for a deletion: Google keeps deleted events as "cancelled" for a while, and this brings one back.</summary>
    public static object RestoreBody() => new { status = "confirmed" };

    private static string CleanTitle(string title) => Truncate(title.Trim(), CalendarEvent.MaxTitleLength);

    private static (DateOnly Start, DateOnly EndExclusive) Dates(CalendarEventDraft draft) =>
        (draft.Date, draft.Date.AddDays(Math.Clamp(draft.Days, 1, 366)));

    private static (DateTimeOffset Start, DateTimeOffset End) Times(CalendarEventDraft draft, TimeZoneInfo timeZone)
    {
        var local = draft.Date.ToDateTime(draft.StartTime);
        var start = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        return (start, TimeZoneInfo.ConvertTime(start + draft.Duration, timeZone));
    }

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
