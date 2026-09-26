using System.Text.Json.Serialization;

namespace FamilyHub.Core.Household;

/// <summary>
/// The fixed, calm palette for family members. Colours are defined as design tokens
/// (<c>--hub-person-*</c>) so they work in both light and dark theme.
/// </summary>
public enum MemberColor
{
    Sage,
    Sky,
    Terracotta,
    Sand,
    Plum,
    Rose,
    Teal,
    Stone,
}

/// <summary>A person in the household. Modules refer to members by <see cref="Id"/>.</summary>
public sealed record FamilyMember
{
    public const int MaxNameLength = 30;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    public MemberColor Color { get; init; } = MemberColor.Sage;

    /// <summary>"C" for Charlotte, "AM" for Anne Marie.</summary>
    [JsonIgnore]
    public string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Length switch
            {
                0 => "?",
                1 => words[0][..1].ToUpperInvariant(),
                _ => string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant(),
            };
        }
    }
}

/// <summary>Household-wide settings, shared by all screens. Stored in <c>household.json</c>.</summary>
public sealed record HouseholdSettings
{
    public const int MaxNameLength = 40;
    public const int MaxMembers = 12;

    public static HouseholdSettings Empty { get; } = new();

    /// <summary>E.g. "Familien Hansen" – shown in the greeting on the home screen.</summary>
    public string Name { get; init; } = "";

    public IReadOnlyList<FamilyMember> Members { get; init; } = [];

    public FamilyMember? FindMember(Guid id) => Members.FirstOrDefault(m => m.Id == id);

    /// <summary>Cleans up data before it is saved: trims text, drops blanks and duplicates, caps sizes.</summary>
    public HouseholdSettings Normalize()
    {
        var members = (Members ?? [])
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Name))
            .DistinctBy(m => m.Id)
            .Take(MaxMembers)
            .Select(m => m with
            {
                Name = Truncate(m.Name.Trim(), FamilyMember.MaxNameLength),
                Color = Enum.IsDefined(m.Color) ? m.Color : MemberColor.Sage,
            })
            .ToList();

        return this with
        {
            Name = Truncate((Name ?? "").Trim(), MaxNameLength),
            Members = members,
        };
    }

    /// <summary>The first colour not already used – a sensible default for a new member.</summary>
    public MemberColor NextFreeColor() =>
        Enum.GetValues<MemberColor>().FirstOrDefault(c => Members.All(m => m.Color != c));

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
