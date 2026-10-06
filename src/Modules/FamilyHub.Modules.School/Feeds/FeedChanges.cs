using System.Text.Json.Serialization;
using FamilyHub.Core.Time;
using FamilyHub.Modules.School.Schedules;

namespace FamilyHub.Modules.School.Feeds;

public enum FeedChangeKind
{
    /// <summary>A new lesson.</summary>
    Added,

    /// <summary>Same lesson, other time (or day).</summary>
    Moved,

    /// <summary>Same time, other place or name.</summary>
    Changed,

    /// <summary>The lesson is gone from the calendar.</summary>
    Cancelled,
}

/// <summary>
/// A lesson that changed in the school's calendar since the family last looked – kept until someone taps "Markér som set"
/// or the day has passed. <see cref="Before"/> is how it was when first seen changing, <see cref="After"/> how it is now.
/// </summary>
public sealed record FeedChange(string Key, FeedEvent? Before, FeedEvent? After, DateTimeOffset DetectedAt)
{
    [JsonIgnore]
    public FeedChangeKind Kind => (Before, After) switch
    {
        (null, _) => FeedChangeKind.Added,
        (_, null) => FeedChangeKind.Cancelled,
        var (b, a) when b.Start != a.Start || b.End != a.End => FeedChangeKind.Moved,
        _ => FeedChangeKind.Changed,
    };

    /// <summary>The lesson as it is now – or as it was, when cancelled.</summary>
    [JsonIgnore]
    public FeedEvent Shown => After ?? Before!;

    [JsonIgnore]
    public DateOnly Date => DateOnly.FromDateTime(Shown.Start);
}

/// <summary>Finding, keeping and describing changes in a school calendar – pure functions with tests.</summary>
public static class FeedChanges
{
    /// <summary>The feed's own key – or title and start for feeds without UIDs.</summary>
    public static string KeyOf(FeedEvent lesson) => lesson.Key ?? $"{lesson.Title}|{lesson.Start:O}";

    /// <summary>
    /// What changed between two fetches for lessons starting in [<paramref name="from"/>, <paramref name="to"/>) – the week
    /// on the screen from today. A lesson that only got a new UID is not a change; one with a new UID but the same title
    /// on the same day counts as moved.
    /// </summary>
    public static IReadOnlyList<FeedChange> Detect(IReadOnlyList<FeedEvent> before, IReadOnlyList<FeedEvent> after, DateTime from, DateTime to, DateTimeOffset now)
    {
        bool InWindow(FeedEvent e) => e.Start >= from && e.Start < to;
        var old = before.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.First());
        var current = after.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.First());

        var changes = new List<FeedChange>();
        foreach (var (key, was) in old)
        {
            if (current.TryGetValue(key, out var isNow) && (InWindow(was) || InWindow(isNow)) && !Same(was, isNow))
            {
                changes.Add(new FeedChange(key, was, isNow, now));
            }
        }

        var removed = old.Where(kv => !current.ContainsKey(kv.Key)).Select(kv => kv.Value).Where(InWindow).ToList();
        var added = current.Where(kv => !old.ContainsKey(kv.Key)).Select(kv => kv.Value).Where(InWindow).ToList();

        // A new UID for the very same lesson is the school system's business, not the family's.
        foreach (var gone in removed.ToList())
        {
            if (added.FirstOrDefault(a => Same(a, gone)) is { } twin)
            {
                removed.Remove(gone);
                added.Remove(twin);
            }
        }

        foreach (var gone in removed.ToList())
        {
            if (added.FirstOrDefault(a => a.Title == gone.Title && a.Start.Date == gone.Start.Date) is { } moved)
            {
                removed.Remove(gone);
                added.Remove(moved);
                changes.Add(new FeedChange(KeyOf(gone), gone, moved, now));
            }
        }

        changes.AddRange(removed.Select(r => new FeedChange(KeyOf(r), r, null, now)));
        changes.AddRange(added.Select(a => new FeedChange(KeyOf(a), null, a, now)));
        return [.. changes.OrderBy(c => c.Shown.Start)];
    }

    /// <summary>
    /// The changes still waiting plus the new ones: per lesson the first "before" and the latest "after". A lesson back as
    /// it was is no change any more. Changes on days that have passed are dropped.
    /// </summary>
    public static IReadOnlyList<FeedChange> Merge(IReadOnlyList<FeedChange> pending, IReadOnlyList<FeedChange> detected, DateOnly today)
    {
        var byKey = new Dictionary<string, FeedChange>();
        foreach (var change in pending)
        {
            byKey[change.Key] = change;
        }

        foreach (var change in detected)
        {
            var merged = byKey.TryGetValue(change.Key, out var waiting) ? waiting with { After = change.After, DetectedAt = change.DetectedAt } : change;
            if ((merged.Before, merged.After) is (null, null) || (merged.Before is { } b && merged.After is { } a && Same(b, a)))
            {
                byKey.Remove(change.Key);
            }
            else
            {
                byKey[change.Key] = merged;
            }
        }

        return [.. byKey.Values.Where(c => c.Date >= today).OrderBy(c => c.Shown.Start)];
    }

    /// <summary>"Ny", "Flyttet", "Ændret", "Aflyst" – the badge on the lesson.</summary>
    public static string Label(FeedChangeKind kind) => kind switch
    {
        FeedChangeKind.Added => "Ny",
        FeedChangeKind.Moved => "Flyttet",
        FeedChangeKind.Cancelled => "Aflyst",
        _ => "Ændret",
    };

    /// <summary>"Tirsdag: Statistik er flyttet fra 08:15–12:00 til 10:15–12:00" – one line in the warning.</summary>
    public static string Describe(FeedChange change)
    {
        var day = ScheduleRules.DayName(change.Date.DayOfWeek);
        switch (change.Kind)
        {
            case FeedChangeKind.Added:
                return $"{day}: ny – {change.After!.Title} {Time(change.After)}{Place(change.After)}";

            case FeedChangeKind.Cancelled:
                return $"{day}: {change.Before!.Title} {Time(change.Before)} er aflyst";

            case FeedChangeKind.Moved:
                var before = change.Before!;
                var after = change.After!;
                var from = before.Start.Date == after.Start.Date
                    ? Time(before)
                    : $"{ScheduleRules.DayName(before.Start.DayOfWeek).ToLower(DanishFormat.Culture)} {Time(before)}";
                return $"{day}: {after.Title} er flyttet fra {from} til {Time(after)}";

            default:
                var was = change.Before!;
                var now = change.After!;
                return was.Location != now.Location
                    ? $"{day}: {now.Title} {Time(now)} – nyt lokale: {now.Location ?? "intet angivet"}"
                    : $"{day}: {was.Title} {Time(now)} hedder nu {now.Title}";
        }
    }

    private static bool Same(FeedEvent a, FeedEvent b) =>
        a.Start == b.Start && a.End == b.End && a.Title == b.Title && a.Location == b.Location;

    private static string Time(FeedEvent lesson) =>
        DanishFormat.TimeRange(TimeOnly.FromDateTime(lesson.Start), TimeOnly.FromDateTime(lesson.End));

    private static string Place(FeedEvent lesson) => lesson.Location is { } place ? $" · {place}" : "";
}
