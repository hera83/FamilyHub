using System.Text.RegularExpressions;
using FamilyHub.Core.Time;

namespace FamilyHub.Modules.School.Feeds;

/// <summary>One part of a lesson's description: "Underviser" with the names – or, without a label, what the lesson is about.</summary>
public sealed record DetailSection(string? Label, IReadOnlyList<string> Lines);

/// <summary>
/// Makes a calendar description readable on the kitchen screen – a pure function with tests. Moodle writes blocks with an
/// English heading in capitals ("COURSE", "TEACHER") and a list of links at the end:
/// <code>
/// Jura 4 (Samlet hold)
///
/// COURSE
/// 3. semester / Modul 5 - Jura - E26 [1]
///
/// TEACHER
/// Anette Jul Mortensen (anettejm@law.aau.dk)
///
/// Links:
/// ------
/// [1] https://www.moodle.aau.dk/…
/// </code>
/// Headings get Danish names, and links, link numbers and e-mail addresses are left out – the screen can't open them.
/// Other calendars' descriptions become plain paragraphs.
/// </summary>
public static partial class LessonDetails
{
    public static IReadOnlyList<DetailSection> Sections(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var sections = new List<DetailSection>();
        foreach (var block in Blocks(description))
        {
            var lines = block.Select(Clean).Where(l => l.Length > 0 && !IsRule(l)).ToList();
            if (lines.Count > 1 && IsHeading(lines[0]))
            {
                sections.Add(new DetailSection(Label(lines[0], lines.Count - 1), lines[1..]));
            }
            else if (lines.Count > 0 && !(lines.Count == 1 && lines[0].EndsWith(':')))
            {
                sections.Add(new DetailSection(null, lines));
            }
        }

        return sections;
    }

    /// <summary>Blocks are separated by empty lines (or lines with only spaces).</summary>
    private static IEnumerable<List<string>> Blocks(string text)
    {
        var block = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (block.Count > 0)
                {
                    yield return block;
                    block = [];
                }
            }
            else
            {
                block.Add(line);
            }
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }

    /// <summary>
    /// "Anette Jul Mortensen (anettejm@law.aau.dk)" → "Anette Jul Mortensen"; "Modul 5 - E26 [1]" → "Modul 5 - E26";
    /// "Teams: &lt;https://…&gt;" → "Teams:" (a heading without content, so left out).
    /// </summary>
    private static string Clean(string line)
    {
        line = Link().Replace(line, "");
        line = LinkNumber().Replace(line, "");
        line = Email().Replace(line, "");
        line = line.Replace("&amp;", "&", StringComparison.Ordinal);
        return Spaces().Replace(line, " ").Trim();
    }

    /// <summary>"------" under a heading.</summary>
    private static bool IsRule(string line) => line.All(c => c is '-' or '=' or '_' or '*');

    /// <summary>"TEACHER", "COURSE" – or a short line ending in a colon ("Links:", "Husk:").</summary>
    private static bool IsHeading(string line) =>
        CapitalHeading().IsMatch(line) || (line.EndsWith(':') && line.Length <= 30 && line.IndexOf(':') == line.Length - 1);

    private static string Label(string heading, int count) => heading.TrimEnd(':').Trim().ToUpperInvariant() switch
    {
        "COURSE" or "KURSUS" => "Kursus",
        "TEACHER" or "TEACHERS" or "UNDERVISER" or "UNDERVISERE" => count > 1 ? "Undervisere" : "Underviser",
        "GROUP" or "GROUPS" or "HOLD" => "Hold",
        "ROOM" or "LOKALE" => "Lokale",
        "DESCRIPTION" or "BESKRIVELSE" => "Beskrivelse",
        var other => DanishFormat.Capitalize(other.ToLower(DanishFormat.Culture)),
    };

    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex LinkNumber();

    [GeneratedRegex(@"\s*[(<]\s*(?:mailto:)?[^\s()<>@]+@[^\s()<>@]+\s*[)>]")]
    private static partial Regex Email();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"<?(?:https?://|www\.)[^\s>]+>?", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"^\p{Lu}[\p{Lu} ]{3,}:?$")]
    private static partial Regex CapitalHeading();
}
