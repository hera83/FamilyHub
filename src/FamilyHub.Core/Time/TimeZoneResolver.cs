namespace FamilyHub.Core.Time;

internal static class TimeZoneResolver
{
    public static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        // IANA ids ("Europe/Copenhagen") work on both Linux and Windows (via ICU).
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found))
        {
            zone = found;
            return true;
        }

        return false;
    }

    public static TimeZoneInfo Find(string id) =>
        TryFind(id, out var zone)
            ? zone
            : throw new InvalidOperationException($"Ukendt tidszone '{id}'. Brug et IANA-navn som 'Europe/Copenhagen'.");
}
