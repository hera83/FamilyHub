namespace FamilyHub.Core.Time;

/// <summary>
/// The household clock – always in the configured time zone (default Europe/Copenhagen),
/// so the kitchen screen, a laptop and a phone agree on "today".
/// Use this instead of <see cref="DateTime.Now"/> everywhere.
/// </summary>
public interface IHubClock
{
    TimeZoneInfo TimeZone { get; }

    DateTimeOffset Now { get; }

    DateOnly Today { get; }

    /// <summary>
    /// Calls <paramref name="onTick"/> right after every whole minute.
    /// Dispose the returned subscription when the component/service goes away.
    /// The callback runs on a background thread – in components, use <c>InvokeAsync</c>.
    /// </summary>
    IDisposable SubscribeMinuteTick(Action<DateTimeOffset> onTick);
}
