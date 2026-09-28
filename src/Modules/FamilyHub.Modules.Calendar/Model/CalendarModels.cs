using FamilyHub.Core.Household;

namespace FamilyHub.Modules.Calendar.Model;

/// <summary>A calendar found in a connected Google account.</summary>
public sealed record CalendarSource
{
    /// <summary>Google's calendar id, e.g. "heine@gmail.com" or "abc123@group.calendar.google.com".</summary>
    public required string Id { get; init; }

    /// <summary>The Google account the calendar is read through.</summary>
    public required string AccountId { get; init; }

    public required string Name { get; init; }

    /// <summary>The account may add events (owner or writer).</summary>
    public bool CanWrite { get; init; }

    /// <summary>The account's own main calendar.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Shown in Google Calendar itself – used as the default when the calendar is first found.</summary>
    public bool SelectedInGoogle { get; init; }
}

/// <summary>The family's choices for one calendar. Stored in <c>kalender/kalendere.json</c>.</summary>
public sealed record CalendarPreference
{
    public required string CalendarId { get; init; }

    public bool Visible { get; init; } = true;

    /// <summary>The family member the calendar belongs to. Null = shared ("Fælles").</summary>
    public Guid? MemberId { get; init; }

    /// <summary>Colour for shared calendars. Member calendars use the member's colour.</summary>
    public MemberColor Color { get; init; } = MemberColor.Stone;
}

/// <summary>A calendar as the screens show it: source + preferences + family member, resolved.</summary>
public sealed record CalendarEntry(CalendarSource Source, CalendarPreference Preference, FamilyMember? Member)
{
    public string Id => Source.Id;

    public string Name => Source.Name;

    public bool Visible => Preference.Visible;

    public bool CanWrite => Source.CanWrite;

    public MemberColor Color => Member?.Color ?? Preference.Color;

    /// <summary>"Charlotte" or "Fælles".</summary>
    public string OwnerLabel => Member?.Name ?? "Fælles";
}

/// <summary>A connected Google account. The refresh token is encrypted with ASP.NET Data Protection.</summary>
public sealed record GoogleAccount
{
    /// <summary>Google's stable user id ("sub").</summary>
    public required string Id { get; init; }

    public required string Email { get; init; }

    public required string ProtectedRefreshToken { get; init; }

    public DateTimeOffset ConnectedAt { get; init; }

    /// <summary>Google no longer accepts the refresh token (revoked, password changed …).</summary>
    public bool NeedsReconnect { get; init; }
}

public enum CalendarView
{
    Day,
    Week,
    Month,
}

public enum SyncProblem
{
    None,

    /// <summary>Google could not be reached – probably no internet.</summary>
    Offline,

    /// <summary>At least one account must be connected again.</summary>
    NeedsReconnect,

    /// <summary>Something else went wrong; details are in the log.</summary>
    Failed,
}

/// <summary>How fresh the calendar is – shown with an InfoBox, never with toasts.</summary>
public sealed record CalendarSyncStatus
{
    public static CalendarSyncStatus Never { get; } = new();

    public DateTimeOffset? LastSuccess { get; init; }

    public DateTimeOffset? LastAttempt { get; init; }

    public SyncProblem Problem { get; init; }

    public bool IsSyncing { get; init; }
}

/// <summary>A new appointment from the "Tilføj aftale" dialog.</summary>
public sealed record NewCalendarEvent
{
    public required string CalendarId { get; init; }

    public required string Title { get; init; }

    public required DateOnly Date { get; init; }

    public bool IsAllDay { get; init; }

    /// <summary>Start time for timed events.</summary>
    public TimeOnly StartTime { get; init; }

    /// <summary>Length for timed events.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Number of days for all-day events.</summary>
    public int Days { get; init; } = 1;
}
