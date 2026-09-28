using System.Text.Json.Serialization;

namespace FamilyHub.Modules.Calendar.Model;

/// <summary>
/// One appointment, already converted to the household's time zone.
/// Recurring events arrive expanded, so every occurrence is its own <see cref="CalendarEvent"/>.
/// </summary>
public sealed record CalendarEvent
{
    public const int MaxTitleLength = 120;

    /// <summary>Google's event id (the instance id for occurrences of a recurring event).</summary>
    public required string Id { get; init; }

    public required string CalendarId { get; init; }

    public required string Title { get; init; }

    /// <summary>Start in local time. For all-day events: midnight at the start of <see cref="StartDate"/>.</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>End (exclusive) in local time. For all-day events: midnight at the start of <see cref="EndDate"/>.</summary>
    public required DateTimeOffset End { get; init; }

    public bool IsAllDay { get; init; }

    /// <summary>First day the event touches.</summary>
    [JsonIgnore]
    public DateOnly StartDate => DateOnly.FromDateTime(Start.DateTime);

    /// <summary>Day after the last day the event touches (exclusive, like Google's all-day end date).</summary>
    [JsonIgnore]
    public DateOnly EndDate
    {
        get
        {
            var end = DateOnly.FromDateTime(End.DateTime);
            // A timed event ending 10:00 still touches that day; one ending exactly at midnight does not.
            var touchesEndDay = !IsAllDay && End.TimeOfDay > TimeSpan.Zero;
            return touchesEndDay || end <= StartDate ? end.AddDays(1) : end;
        }
    }

    public string? Location { get; init; }

    public string? Description { get; init; }

    /// <summary>True when the event touches <paramref name="day"/>.</summary>
    public bool OccursOn(DateOnly day) => day >= StartDate && day < EndDate;

    /// <summary>Same event in the same calendar – used to merge results from several accounts or fetches.</summary>
    [JsonIgnore]
    public string Key => $"{CalendarId}\n{Id}";
}
