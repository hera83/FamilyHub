namespace FamilyHub.Modules.School.Schedules;

/// <summary>The kind of school – decides the grade steps, the class name and what a row is called.</summary>
public enum SchoolLevel
{
    /// <summary>Folkeskole: 0.–10. klasse, "6.a", lessons ("1. time"). The default, also for older files.</summary>
    PrimarySchool,

    /// <summary>Gymnasium: 1.g–3.g, "2.g" or "2.b", modules ("1. modul").</summary>
    Gymnasium,

    /// <summary>Universitet: 1.–12. semester, modules – the courses are added by hand.</summary>
    University,
}

/// <summary>Everything that differs between folkeskole, gymnasium and universitet – in one place.</summary>
public static class SchoolLevels
{
    public static IReadOnlyList<SchoolLevel> All { get; } = Enum.GetValues<SchoolLevel>();

    /// <summary>"Folkeskole", "Gymnasium", "Universitet".</summary>
    public static string Label(this SchoolLevel level) => level switch
    {
        SchoolLevel.Gymnasium => "Gymnasium",
        SchoolLevel.University => "Universitet",
        _ => "Folkeskole",
    };

    public static int MinGrade(this SchoolLevel level) => level == SchoolLevel.PrimarySchool ? 0 : 1;

    public static int MaxGrade(this SchoolLevel level) => level switch
    {
        SchoolLevel.Gymnasium => 3,
        SchoolLevel.University => 12,
        _ => 10,
    };

    /// <summary>Where the grade stepper starts for a new schedule.</summary>
    public static int DefaultGrade(this SchoolLevel level) => 1;

    /// <summary>The stepper's label: "Klassetrin", "Årgang", "Semester".</summary>
    public static string GradeLabel(this SchoolLevel level) => level switch
    {
        SchoolLevel.Gymnasium => "Årgang",
        SchoolLevel.University => "Semester",
        _ => "Klassetrin",
    };

    /// <summary>The stepper's value: "6. klasse", "0. klasse (bh.)", "2.g", "3. semester".</summary>
    public static string GradeText(this SchoolLevel level, int grade) => level switch
    {
        SchoolLevel.Gymnasium => $"{grade}.g",
        SchoolLevel.University => $"{grade}. semester",
        _ => grade == 0 ? "0. klasse (bh.)" : $"{grade}. klasse",
    };

    /// <summary>Folkeskole and gymnasium classes have a letter ("6.a", "2.b"); a university has none.</summary>
    public static bool HasClassLetter(this SchoolLevel level) => level != SchoolLevel.University;

    /// <summary>"6.a", "3. klasse", "2.b", "2.g", "3. semester".</summary>
    public static string ClassName(this SchoolLevel level, int grade, string? letter)
    {
        var hasLetter = level.HasClassLetter() && !string.IsNullOrWhiteSpace(letter);
        return level switch
        {
            SchoolLevel.University => level.GradeText(grade),
            _ when hasLetter => $"{grade}.{letter!.Trim().ToLowerInvariant()}",
            SchoolLevel.Gymnasium => $"{grade}.g",
            _ => $"{grade}. klasse",
        };
    }

    /// <summary>Gymnasium and university can fetch the lessons from the school's calendar (an iCal link) – optional.</summary>
    public static bool SupportsFeed(this SchoolLevel level) => level != SchoolLevel.PrimarySchool;

    /// <summary>What a row is called: "time" in folkeskolen, "modul" at gymnasium and university.</summary>
    public static string LessonWord(this SchoolLevel level) => level == SchoolLevel.PrimarySchool ? "time" : "modul";

    /// <summary>"1 time", "32 timer", "1 modul", "8 moduler".</summary>
    public static string LessonCount(this SchoolLevel level, int count) =>
        level == SchoolLevel.PrimarySchool
            ? $"{count} {(count == 1 ? "time" : "timer")}"
            : $"{count} {(count == 1 ? "modul" : "moduler")}";
}
