using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyHub.Modules.School.Feeds;

/// <summary>One timed event from an iCalendar feed (a single occurrence – repeating events are already expanded).</summary>
/// <param name="Key">
/// Stays the same when the lesson is moved or renamed, so a change can be told from "cancelled + new": the UID – plus the
/// original start for one occurrence of a repeating event. Null when the feed has no UIDs.
/// </param>
public sealed record IcsOccurrence(DateTimeOffset Start, DateTimeOffset End, string Title, string? Location, string? Key = null);

/// <summary>The text was not an iCalendar file (e.g. a login page from an expired link).</summary>
public sealed class IcsFormatException(string message) : Exception(message);

/// <summary>
/// Reads the iCalendar files (RFC 5545) that school systems export – Moodle, TimeEdit, Google and Outlook calendars.
/// Only what a timetable needs: timed events with title, place and times (UTC, TZID or floating), weekly and daily
/// repetition (INTERVAL, COUNT, UNTIL, BYDAY), EXDATE and moved occurrences (RECURRENCE-ID). Leaves out all-day events,
/// cancelled events and events shorter than 10 minutes – Moodle's deadlines have no length.
/// </summary>
public static partial class IcsParser
{
    /// <summary>Shorter is a deadline or a reminder, not a lesson.</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromMinutes(10);

    /// <summary>Longer is an excursion or a project day – not something to put in a timetable row.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(12);

    private const int MaxOccurrencesPerSeries = 2000;

    private sealed record Property(string Name, IReadOnlyDictionary<string, string> Parameters, string Value);

    private sealed record WallTime(DateTime Wall, TimeZoneInfo Zone, bool IsDate)
    {
        public DateTimeOffset ToOffset() => Zone == TimeZoneInfo.Utc
            ? new DateTimeOffset(DateTime.SpecifyKind(Wall, DateTimeKind.Unspecified), TimeSpan.Zero)
            : new DateTimeOffset(DateTime.SpecifyKind(Wall, DateTimeKind.Unspecified), Zone.GetUtcOffset(Wall));
    }

    /// <summary>Every occurrence that overlaps [<paramref name="from"/>, <paramref name="to"/>), oldest first.</summary>
    /// <param name="fallbackZone">For floating times and unknown TZIDs – the household's time zone.</param>
    public static IReadOnlyList<IcsOccurrence> Parse(string text, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo fallbackZone)
    {
        var events = ReadEvents(text);

        // Occurrences moved or cancelled one at a time (RECURRENCE-ID) replace that occurrence of their series.
        var replaced = events
            .Where(e => Get(e, "RECURRENCE-ID") is not null && Get(e, "UID") is not null)
            .Select(e => (Uid: Get(e, "UID")!.Value, At: ParseTime(Get(e, "RECURRENCE-ID")!, fallbackZone)?.ToOffset()))
            .Where(x => x.At is not null)
            .Select(x => (x.Uid, At: x.At!.Value))
            .ToHashSet();

        var result = new List<IcsOccurrence>();
        foreach (var properties in events)
        {
            if (string.Equals(Get(properties, "STATUS")?.Value, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                || Get(properties, "DTSTART") is not { } startProperty
                || ParseTime(startProperty, fallbackZone) is not { IsDate: false } start)
            {
                continue;
            }

            var length = Length(properties, start, fallbackZone);
            if (length < MinLength || length > MaxLength)
            {
                continue;
            }

            var title = Unescape(Get(properties, "SUMMARY")?.Value) is { Length: > 0 } summary ? summary : "Uden titel";
            var location = Unescape(Get(properties, "LOCATION")?.Value) is { Length: > 0 } place ? place : null;
            var uid = Get(properties, "UID")?.Value;
            var isOverride = Get(properties, "RECURRENCE-ID") is not null;
            var repeats = !isOverride && Get(properties, "RRULE") is not null;
            var originalStart = isOverride ? ParseTime(Get(properties, "RECURRENCE-ID")!, fallbackZone)?.ToOffset() : null;
            var excluded = ExcludedStarts(properties, fallbackZone);

            foreach (var occurrenceStart in Occurrences(properties, start, to, isOverride))
            {
                var at = occurrenceStart.ToOffset();
                if (excluded.Contains(at) || (!isOverride && uid is not null && replaced.Contains((uid, at))))
                {
                    continue;
                }

                var end = at + length;
                if (end > from && at < to)
                {
                    var key = uid is null ? null
                        : repeats ? $"{uid}@{at.UtcTicks}"
                        : originalStart is { } original ? $"{uid}@{original.UtcTicks}"
                        : uid;
                    result.Add(new IcsOccurrence(at, end, title, location, key));
                }
            }
        }

        return [.. result.OrderBy(o => o.Start).ThenBy(o => o.Title, StringComparer.Ordinal)];
    }

    // ------------------------------------------------------------------ reading

    private static List<List<Property>> ReadEvents(string text)
    {
        var lines = Unfold(text);
        if (!lines.Any(l => l.Trim().Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
        {
            throw new IcsFormatException("Teksten er ikke en iCalendar-kalender.");
        }

        var events = new List<List<Property>>();
        List<Property>? current = null;
        var nested = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null && line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    current = [];
                }
                else if (current is not null)
                {
                    nested++; // VALARM and friends inside an event
                }

                continue;
            }

            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && nested > 0)
                {
                    nested--;
                }
                else if (current is not null && line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(current);
                    current = null;
                }

                continue;
            }

            if (current is not null && nested == 0 && ParseProperty(line) is { } property)
            {
                current.Add(property);
            }
        }

        return events;
    }

    /// <summary>Long lines are folded: a line starting with a space or tab continues the one before.</summary>
    private static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0)
            {
                lines[^1] += raw[1..];
            }
            else
            {
                lines.Add(raw.TrimEnd());
            }
        }

        return lines;
    }

    /// <summary>NAME;PARAM=value;PARAM="quoted:value":VALUE</summary>
    private static Property? ParseProperty(string line)
    {
        var inQuotes = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ':' && !inQuotes)
            {
                colon = i;
                break;
            }
        }

        if (colon <= 0)
        {
            return null;
        }

        var head = SplitOutsideQuotes(line[..colon], ';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in head.Skip(1))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                parameters[part[..eq].Trim()] = part[(eq + 1)..].Trim().Trim('"');
            }
        }

        return new Property(head[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    private static List<string> SplitOutsideQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }

            if (c == separator && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static Property? Get(List<Property> properties, string name) => properties.FirstOrDefault(p => p.Name == name);

    /// <summary>"Lokale 2.1\, Fib 14" → "Lokale 2.1, Fib 14". Line breaks become spaces – a cell has one line.</summary>
    private static string Unescape(string? value) => value is null
        ? ""
        : value.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    // ------------------------------------------------------------------ times

    private static WallTime? ParseTime(Property property, TimeZoneInfo fallbackZone) =>
        ParseTimeValue(property.Value.Split(',')[0], property.Parameters, fallbackZone);

    private static WallTime? ParseTimeValue(string value, IReadOnlyDictionary<string, string> parameters, TimeZoneInfo fallbackZone)
    {
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new WallTime(date, fallbackZone, IsDate: true);
        }

        var utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(utc ? value[..^1] : value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return null;
        }

        var zone = utc ? TimeZoneInfo.Utc : parameters.TryGetValue("TZID", out var id) ? FindZone(id) ?? fallbackZone : fallbackZone;
        return new WallTime(wall, zone, IsDate: false);
    }

    /// <summary>IANA ("Europe/Copenhagen") and Windows ("Romance Standard Time") names; Outlook sometimes adds a leading "/".</summary>
    private static TimeZoneInfo? FindZone(string id)
    {
        id = id.Trim().Trim('"').TrimStart('/');
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
    }

    private static TimeSpan Length(List<Property> properties, WallTime start, TimeZoneInfo fallbackZone)
    {
        if (Get(properties, "DTEND") is { } endProperty && ParseTime(endProperty, fallbackZone) is { } end)
        {
            return end.ToOffset() - start.ToOffset();
        }

        return Get(properties, "DURATION") is { } duration ? ParseDuration(duration.Value) : TimeSpan.Zero;
    }

    /// <summary>"PT1H45M", "P1DT2H", "P1W".</summary>
    internal static TimeSpan ParseDuration(string value)
    {
        var match = DurationPattern().Match(value.Trim());
        if (!match.Success)
        {
            return TimeSpan.Zero;
        }

        int Part(int group) => match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
        var length = new TimeSpan(Part(2) * 7 + Part(3), Part(4), Part(5), Part(6));
        return match.Groups[1].Value == "-" ? TimeSpan.Zero : length;
    }

    [GeneratedRegex(@"^([+-]?)P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    private static HashSet<DateTimeOffset> ExcludedStarts(List<Property> properties, TimeZoneInfo fallbackZone)
    {
        var excluded = new HashSet<DateTimeOffset>();
        foreach (var property in properties.Where(p => p.Name == "EXDATE"))
        {
            foreach (var value in property.Value.Split(','))
            {
                if (ParseTimeValue(value, property.Parameters, fallbackZone) is { IsDate: false } time)
                {
                    excluded.Add(time.ToOffset());
                }
            }
        }

        return excluded;
    }

    // ------------------------------------------------------------------ repetition

    /// <summary>The start of every occurrence up to <paramref name="to"/> – in the event's own wall clock, so summer time is right.</summary>
    private static IEnumerable<WallTime> Occurrences(List<Property> properties, WallTime start, DateTimeOffset to, bool isOverride)
    {
        var rule = isOverride ? null : Get(properties, "RRULE")?.Value;
        if (rule is null)
        {
            yield return start;
            yield break;
        }

        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => p[1].Trim().ToUpperInvariant());

        var frequency = parts.GetValueOrDefault("FREQ");
        if (frequency is not ("WEEKLY" or "DAILY"))
        {
            yield return start; // Monthly and yearly lessons don't exist – show the first one only.
            yield break;
        }

        var interval = Math.Max(1, int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var i) ? i : 1);
        var count = int.TryParse(parts.GetValueOrDefault("COUNT"), out var c) ? c : int.MaxValue;
        var until = parts.TryGetValue("UNTIL", out var untilText) ? ParseTimeValue(untilText, new Dictionary<string, string>(), start.Zone) : null;
        List<DayOfWeek> days = frequency == "WEEKLY" && parts.TryGetValue("BYDAY", out var byDay)
            ? [.. byDay.Split(',').Select(ToDay).OfType<DayOfWeek>().Distinct().OrderBy(d => ((int)d + 6) % 7)]
            : [];
        if (days.Count == 0)
        {
            days.Add(start.Wall.DayOfWeek);
        }

        var produced = 0;
        var weekStart = start.Wall.Date.AddDays(-(((int)start.Wall.DayOfWeek + 6) % 7));
        for (var step = 0; produced < count && produced < MaxOccurrencesPerSeries && step < MaxOccurrencesPerSeries; step++)
        {
            IEnumerable<DateTime> candidates = frequency == "DAILY"
                ? [start.Wall.AddDays((double)step * interval)]
                : days.Select(d => weekStart.AddDays(step * 7 * interval + ((int)d + 6) % 7) + start.Wall.TimeOfDay);

            foreach (var wall in candidates)
            {
                if (wall < start.Wall)
                {
                    continue;
                }

                var occurrence = start with { Wall = wall };
                if (until is not null && (until.IsDate ? wall.Date > until.Wall.Date : occurrence.ToOffset() > until.ToOffset()))
                {
                    yield break;
                }

                if (occurrence.ToOffset() >= to || produced >= count)
                {
                    yield break;
                }

                produced++;
                yield return occurrence;
            }
        }
    }

    private static DayOfWeek? ToDay(string value)
    {
        // "MO", "TU" … – a leading number ("1MO") only matters for monthly rules.
        var code = value.Trim().TrimStart('+', '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return code switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };
    }
}
