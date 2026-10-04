using FamilyHub.Core.Dexcom;

namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>The readings kept in memory: sorted by time, oldest first. Pure functions – unit-tested.</summary>
internal static class GlucoseHistory
{
    /// <summary>
    /// Puts a freshly fetched window into the history. The API's answer is the truth for the window, so
    /// readings strictly inside it that the API no longer has are dropped; a reading at the same time is replaced.
    /// Late readings (Dexcom fills gaps after a lost signal) land in their right place.
    /// Everything older than <paramref name="keepFrom"/> is dropped.
    /// </summary>
    public static GlucoseReading[] Merge(
        GlucoseReading[] existing, DateTimeOffset from, DateTimeOffset to, IReadOnlyList<GlucoseReading> fetched, DateTimeOffset keepFrom)
    {
        var byTime = new SortedDictionary<DateTimeOffset, GlucoseReading>(); // compares instants, whatever the offset
        foreach (var reading in existing)
        {
            var insideWindow = reading.Time > from && reading.Time < to;
            if (!insideWindow && reading.Time >= keepFrom)
            {
                byTime[reading.Time] = reading;
            }
        }

        foreach (var reading in fetched)
        {
            if (reading.Time >= keepFrom)
            {
                byTime[reading.Time] = reading;
            }
        }

        return [.. byTime.Values];
    }

    /// <summary>The readings from <paramref name="from"/> up to and including <paramref name="to"/> – without copying.</summary>
    public static IReadOnlyList<GlucoseReading> Slice(GlucoseReading[] sorted, DateTimeOffset from, DateTimeOffset to)
    {
        var start = FirstAtOrAfter(sorted, from);
        var end = FirstAfter(sorted, to);
        return end <= start ? ArraySegment<GlucoseReading>.Empty : new ArraySegment<GlucoseReading>(sorted, start, end - start);
    }

    private static int FirstAtOrAfter(GlucoseReading[] sorted, DateTimeOffset time)
    {
        int low = 0, high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid].Time < time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static int FirstAfter(GlucoseReading[] sorted, DateTimeOffset time)
    {
        int low = 0, high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid].Time <= time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
