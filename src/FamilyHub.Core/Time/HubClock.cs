using FamilyHub.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Time;

/// <summary>
/// Singleton clock with one shared minute timer for all connected screens.
/// </summary>
internal sealed class HubClock : IHubClock, IDisposable
{
    // Fire slightly after the boundary so "Now" is guaranteed to be in the new minute.
    private static readonly TimeSpan BoundaryMargin = TimeSpan.FromMilliseconds(40);

    private readonly TimeProvider time;
    private readonly ILogger<HubClock> logger;
    private readonly Lock gate = new();
    private readonly ITimer timer;
    private Action<DateTimeOffset>[] subscribers = [];

    public HubClock(TimeProvider time, IOptions<FamilyHubOptions> options, ILogger<HubClock> logger)
    {
        this.time = time;
        this.logger = logger;
        TimeZone = TimeZoneResolver.Find(options.Value.TimeZone);
        timer = time.CreateTimer(OnTimer, null, DelayUntilNextMinute(), Timeout.InfiniteTimeSpan);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public IDisposable SubscribeMinuteTick(Action<DateTimeOffset> onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        lock (gate)
        {
            subscribers = [.. subscribers, onTick];
        }

        return new Subscription(this, onTick);
    }

    public void Dispose() => timer.Dispose();

    private void Unsubscribe(Action<DateTimeOffset> onTick)
    {
        lock (gate)
        {
            subscribers = [.. subscribers.Where(s => s != onTick)];
        }
    }

    private void OnTimer(object? state)
    {
        var now = Now;
        Action<DateTimeOffset>[] snapshot;
        lock (gate)
        {
            snapshot = subscribers;
        }

        foreach (var subscriber in snapshot)
        {
            try
            {
                subscriber(now);
            }
            catch (Exception ex)
            {
                // One broken subscriber must never stop the clock for everyone else.
                logger.LogWarning(ex, "Minute tick subscriber failed");
            }
        }

        timer.Change(DelayUntilNextMinute(), Timeout.InfiniteTimeSpan);
    }

    private TimeSpan DelayUntilNextMinute()
    {
        var now = time.GetUtcNow();
        var nextMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(1);
        return nextMinute - now + BoundaryMargin;
    }

    private sealed class Subscription(HubClock owner, Action<DateTimeOffset> onTick) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Unsubscribe(onTick);
            }
        }
    }
}
