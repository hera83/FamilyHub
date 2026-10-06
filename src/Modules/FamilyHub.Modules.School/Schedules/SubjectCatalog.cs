using FamilyHub.Core.Household;

namespace FamilyHub.Modules.School.Schedules;

/// <summary>
/// The subjects a new schedule starts with: the folkeskole's subjects for the grade, or the gymnasium's common subjects –
/// so most families only remove a few and add their school's own (e.g. "Morgensang"). A university schedule starts
/// empty: the courses are the student's own.
/// The big subjects always get the same colour, so Dansk looks the same in every child's schedule.
/// </summary>
public static class SubjectCatalog
{
    private sealed record Preset(string Name, int FromGrade, int ToGrade, MemberColor? Color = null);

    private static readonly Preset[] PrimarySchool =
    [
        new("Bh. klasse", 0, 0, MemberColor.Sage),
        new("Morgensang", 0, 0, MemberColor.Sand),
        new("Bibliotek", 0, 0, MemberColor.Sky),
        new("Dansk", 1, 10, MemberColor.Terracotta),
        new("Matematik", 1, 10, MemberColor.Sky),
        new("Engelsk", 1, 10, MemberColor.Rose),
        new("Tysk", 5, 10, MemberColor.Sand),
        new("Natur/teknologi", 1, 6, MemberColor.Sage),
        new("Biologi", 7, 10, MemberColor.Sage),
        new("Geografi", 7, 10),
        new("Fysik/kemi", 7, 10),
        new("Kristendom", 1, 10, MemberColor.Plum),
        new("Historie", 3, 10, MemberColor.Stone),
        new("Samfundsfag", 8, 10),
        new("Idræt", 0, 10, MemberColor.Teal),
        new("Musik", 0, 6),
        new("Billedkunst", 1, 5),
        new("Håndværk og design", 4, 7),
        new("Madkundskab", 4, 7),
        new("Valgfag", 7, 10),
        new("Klassens tid", 1, 10),
    ];

    private static readonly Preset[] Gymnasium =
    [
        new("Dansk", 1, 3, MemberColor.Terracotta),
        new("Matematik", 1, 3, MemberColor.Sky),
        new("Engelsk", 1, 3, MemberColor.Rose),
        new("Tysk", 1, 3, MemberColor.Sand),
        new("Fransk", 1, 3),
        new("Spansk", 1, 3),
        new("Historie", 1, 3, MemberColor.Stone),
        new("Samfundsfag", 1, 3),
        new("Biologi", 1, 3, MemberColor.Sage),
        new("Kemi", 1, 3),
        new("Fysik", 1, 3),
        new("Naturgeografi", 1, 1),
        new("Religion", 2, 3, MemberColor.Plum),
        new("Oldtidskundskab", 2, 3),
        new("Idræt", 1, 3, MemberColor.Teal),
        new("Musik", 1, 3),
        new("Billedkunst", 1, 3),
    ];

    /// <summary>The usual subjects for the kind of school and the grade, each with its colour.</summary>
    public static IReadOnlyList<SchoolSubject> For(SchoolLevel level, int grade)
    {
        Preset[] presets = level switch
        {
            SchoolLevel.PrimarySchool => PrimarySchool,
            SchoolLevel.Gymnasium => Gymnasium,
            _ => [],
        };

        var subjects = new List<SchoolSubject>();
        foreach (var preset in presets.Where(p => grade >= p.FromGrade && grade <= p.ToGrade))
        {
            subjects.Add(new SchoolSubject { Name = preset.Name, Color = preset.Color ?? NextColor(subjects) });
        }

        return subjects;
    }

    /// <summary>The colour used least in the schedule – a sensible default for a new subject.</summary>
    public static MemberColor NextColor(IEnumerable<SchoolSubject> existing)
    {
        var counts = existing.GroupBy(s => s.Color).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetValues<MemberColor>().MinBy(c => counts.GetValueOrDefault(c));
    }
}
