using System.Globalization;
using System.Text.RegularExpressions;
using FamilyHub.Core.Household;
using FamilyHub.Modules.Calendar.Model;

namespace FamilyHub.Modules.Calendar.Services;

/// <summary>Sensible defaults for a newly found calendar, so most families never have to adjust anything.</summary>
internal static class CalendarMatching
{
    private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

    /// <summary>
    /// The family member a calendar most likely belongs to: "Emma", "Emmas fodbold" or "heine.ramskov@gmail.com"
    /// match on the first name. Null when nobody – or more than one – matches.
    /// </summary>
    public static FamilyMember? GuessMember(CalendarSource source, IReadOnlyList<FamilyMember> members)
    {
        var words = Words(source.Name).Concat(source.IsPrimary ? Words(source.Id.Split('@')[0]) : []).ToHashSet();
        var matches = members
            .Where(m => Words(m.Name).FirstOrDefault() is { } first && (words.Contains(first) || words.Contains(first + "s")))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The first colour not used by a family member or another shared calendar.</summary>
    public static MemberColor NextSharedColor(IReadOnlyList<FamilyMember> members, IEnumerable<CalendarPreference> preferences)
    {
        var used = members.Select(m => m.Color)
            .Concat(preferences.Where(p => p.MemberId is null).Select(p => p.Color))
            .ToHashSet();
        return Enum.GetValues<MemberColor>().Where(c => !used.Contains(c)).DefaultIfEmpty(MemberColor.Stone).First();
    }

    // Letters only: "heine.ramskov2020@gmail.com" → heine, ramskov, gmail, com.
    private static IEnumerable<string> Words(string text) =>
        Regex.Split(text, @"[^\p{L}]+")
            .Where(w => w.Length > 1)
            .Select(w => w.ToLower(Danish));
}
