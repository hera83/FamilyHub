using System.Text.Json.Serialization;
using FamilyHub.Core.Household;
using FamilyHub.Modules.School.Feeds;

namespace FamilyHub.Modules.School.Schedules;

public enum PeriodKind
{
    /// <summary>A lesson – "3. time". Has a cell for every school day.</summary>
    Lesson,

    /// <summary>A break – "Frikvarter". Shown as a band across the whole week.</summary>
    Break,
}

/// <summary>One row in the timetable: a lesson or a break, with its times.</summary>
public sealed record SchoolPeriod
{
    public const int MaxLabelLength = 24;

    public Guid Id { get; init; } = Guid.NewGuid();

    public PeriodKind Kind { get; init; }

    public TimeOnly Start { get; init; }

    public TimeOnly End { get; init; }

    /// <summary>"Frikvarter", "Spisepause" … – only for breaks.</summary>
    public string? Label { get; init; }

    [JsonIgnore]
    public bool IsBreak => Kind == PeriodKind.Break;

    [JsonIgnore]
    public int Minutes => (int)(End - Start).TotalMinutes;
}

/// <summary>A subject the family can pick in this schedule, with its colour in the timetable.</summary>
public sealed record SchoolSubject
{
    public const int MaxNameLength = 30;

    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; init; }

    /// <summary>The same calm palette as the family members (<c>--hub-person-*</c>), so night mode just works.</summary>
    public MemberColor Color { get; init; } = MemberColor.Stone;
}

/// <summary>
/// What happens in one lesson on one day. Most cells have one subject every week; some alternate between
/// odd and even weeks ("Billedkunst uge 1 / Tysk uge 2").
/// </summary>
public sealed record ScheduleCell
{
    public const int MaxNoteLength = 40;

    public DayOfWeek Day { get; init; }

    public Guid PeriodId { get; init; }

    /// <summary>The subject every week – or in the odd weeks when <see cref="AlternatesWeekly"/>.</summary>
    public Guid? SubjectId { get; init; }

    public bool AlternatesWeekly { get; init; }

    /// <summary>The subject in the even weeks. Only used when <see cref="AlternatesWeekly"/>.</summary>
    public Guid? EvenWeekSubjectId { get; init; }

    /// <summary>"Lokale 64", "Husk idrætstøj" – shown under the subject.</summary>
    public string? Note { get; init; }

    /// <summary>The subject in the given ISO week (odd/even decides when the cell alternates).</summary>
    public Guid? SubjectInWeek(int isoWeek) => AlternatesWeekly && isoWeek % 2 == 0 ? EvenWeekSubjectId : SubjectId;

    [JsonIgnore]
    public bool IsEmpty => SubjectId is null && EvenWeekSubjectId is null && string.IsNullOrWhiteSpace(Note);
}

/// <summary>
/// One lesson on one date as the family has changed it on the school page: hidden (e.g. cancelled) and/or a note for
/// that day only. A lesson in the fixed timetable is its row on the date; one from the school's calendar is its key
/// (<see cref="FeedChanges.KeyOf"/> – one occurrence, so it follows the lesson if it is moved).
/// </summary>
public sealed record LessonAdjustment
{
    public const int MaxNoteLength = 60;
    public const int MaxTitleLength = 120;

    /// <summary>The lesson's date – decides which week the adjustment belongs to.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The row in the fixed timetable – null for a lesson from the calendar.</summary>
    public Guid? PeriodId { get; init; }

    /// <summary>The calendar lesson's key – null for a lesson in the fixed timetable.</summary>
    public string? LessonKey { get; init; }

    public bool Hidden { get; init; }

    /// <summary>"Husk madpakke" – shown under the lesson that day, after the timetable's own note.</summary>
    public string? Note { get; init; }

    /// <summary>The subject when it was adjusted – for the list of hidden lessons, also if the lesson is gone later.</summary>
    public string Title { get; init; } = "";

    public TimeOnly Start { get; init; }

    public TimeOnly End { get; init; }

    [JsonIgnore]
    public bool IsEmpty => !Hidden && string.IsNullOrWhiteSpace(Note);

    /// <summary>A lesson in the fixed timetable on a date.</summary>
    public static LessonAdjustment ForPeriod(DateOnly date, SchoolPeriod period, string title) =>
        new() { Date = date, PeriodId = period.Id, Title = title, Start = period.Start, End = period.End };

    /// <summary>A lesson from the school's calendar.</summary>
    public static LessonAdjustment ForLesson(FeedEvent lesson) => new()
    {
        Date = DateOnly.FromDateTime(lesson.Start),
        LessonKey = FeedChanges.KeyOf(lesson),
        Title = lesson.Title,
        Start = TimeOnly.FromDateTime(lesson.Start),
        End = TimeOnly.FromDateTime(lesson.End),
    };

    /// <summary>True when both are about the same lesson (the calendar key – or the row on the same date).</summary>
    public bool IsSameLesson(LessonAdjustment other) => LessonKey is not null
        ? LessonKey == other.LessonKey
        : other.LessonKey is null && PeriodId == other.PeriodId && Date == other.Date;
}

/// <summary>One child's weekly timetable, Monday to Friday.</summary>
public sealed record SchoolSchedule
{
    public const int MaxPeriods = 16;
    public const int MaxSubjects = 30;

    /// <summary>Plenty for a few weeks ahead – the newest are kept.</summary>
    public const int MaxAdjustments = 300;

    /// <summary>The class letters offered in the settings – "6.a", "0.b", "2.b".</summary>
    public static IReadOnlyList<string> ClassLetters { get; } = ["A", "B", "C", "D", "E", "F"];

    public static IReadOnlyList<DayOfWeek> SchoolDays { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The child (<see cref="FamilyMember.Id"/>) – gives the name and colour.</summary>
    public Guid MemberId { get; init; }

    /// <summary>Folkeskole, gymnasium or universitet – missing in older files, which are all folkeskole.</summary>
    public SchoolLevel Level { get; init; }

    /// <summary>The class (0–10), the gymnasium year (1–3) or the semester (1–12) – see <see cref="SchoolLevels"/>.</summary>
    public int Grade { get; init; }

    /// <summary>"A", "B" … or null. Not used at a university.</summary>
    public string? ClassLetter { get; init; }

    public IReadOnlyList<SchoolSubject> Subjects { get; init; } = [];

    /// <summary>Lessons and breaks, ordered by start time.</summary>
    public IReadOnlyList<SchoolPeriod> Periods { get; init; } = [];

    public IReadOnlyList<ScheduleCell> Cells { get; init; } = [];

    /// <summary>Lessons hidden or given a note on one date – from this week on (older ones are dropped on the next change).</summary>
    public IReadOnlyList<LessonAdjustment> Adjustments { get; init; } = [];

    /// <summary>
    /// Optional iCalendar link (e.g. Moodle's calendar export) – then the lessons come from the school's calendar and keep
    /// themselves up to date. It holds a private key, so it stays on the server and the screen only shows the host.
    /// </summary>
    public string? FeedUrl { get; init; }

    /// <summary>True when the lessons come from <see cref="FeedUrl"/> (gymnasium and university only).</summary>
    [JsonIgnore]
    public bool UsesFeed => Level.SupportsFeed() && FeedUrl is not null;

    /// <summary>"6.a", "3. klasse", "2.g" or "3. semester".</summary>
    [JsonIgnore]
    public string ClassName => Level.ClassName(Grade, ClassLetter);

    public SchoolSubject? FindSubject(Guid? id) => id is { } value ? Subjects.FirstOrDefault(s => s.Id == value) : null;

    public SchoolPeriod? FindPeriod(Guid id) => Periods.FirstOrDefault(p => p.Id == id);

    public ScheduleCell? CellAt(DayOfWeek day, Guid periodId) => Cells.FirstOrDefault(c => c.Day == day && c.PeriodId == periodId);

    /// <summary>The cell replaced (or removed, when empty) – everything else untouched.</summary>
    public SchoolSchedule WithCell(ScheduleCell cell)
    {
        var others = Cells.Where(c => c.Day != cell.Day || c.PeriodId != cell.PeriodId);
        return this with { Cells = cell.IsEmpty ? [.. others] : [.. others, cell] };
    }

    /// <summary>What the family has done to the lesson – null when nothing.</summary>
    public LessonAdjustment? FindAdjustment(LessonAdjustment lesson) => Adjustments.FirstOrDefault(a => a.IsSameLesson(lesson));

    /// <summary>
    /// Changes one lesson's adjustment, starting from the one saved (or <paramref name="lesson"/>), so a note and "Skjul" from
    /// two screens never overwrite each other. Removed when it ends up empty; adjustments before <paramref name="keepFrom"/> go.
    /// </summary>
    public SchoolSchedule WithAdjustment(LessonAdjustment lesson, Func<LessonAdjustment, LessonAdjustment> change, DateOnly keepFrom)
    {
        var updated = change(FindAdjustment(lesson) ?? lesson);
        var others = Adjustments.Where(a => !a.IsSameLesson(lesson) && a.Date >= keepFrom);
        return this with { Adjustments = updated.IsEmpty ? [.. others] : [.. others, updated] };
    }

    /// <summary>The plain timetable, as the settings show it – nothing hidden, no notes for a single day.</summary>
    public SchoolSchedule WithoutAdjustments() => this with { Adjustments = [] };

    /// <summary>
    /// Cleans up before saving – also a file edited by hand: valid grade and letter, trimmed names,
    /// periods in order without zero-length rows, and no cells pointing at rows or subjects that are gone.
    /// </summary>
    public SchoolSchedule Normalized()
    {
        var subjects = (Subjects ?? [])
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => s with
            {
                Name = Truncate(s.Name.Trim(), SchoolSubject.MaxNameLength),
                Color = Enum.IsDefined(s.Color) ? s.Color : MemberColor.Stone,
            })
            .DistinctBy(s => s.Id)
            .DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSubjects)
            .ToList();

        var periods = (Periods ?? [])
            .Where(p => p is not null && p.End > p.Start && Enum.IsDefined(p.Kind))
            .DistinctBy(p => p.Id)
            .Select(p => p with
            {
                Label = p.IsBreak ? Truncate(string.IsNullOrWhiteSpace(p.Label) ? "Pause" : p.Label.Trim(), SchoolPeriod.MaxLabelLength) : null,
            })
            .OrderBy(p => p.Start)
            .ThenBy(p => p.End)
            .Take(MaxPeriods)
            .ToList();

        var lessonIds = periods.Where(p => !p.IsBreak).Select(p => p.Id).ToHashSet();
        var subjectIds = subjects.Select(s => s.Id).ToHashSet();
        Guid? Known(Guid? id) => id is { } value && subjectIds.Contains(value) ? value : null;

        var cells = (Cells ?? [])
            .Where(c => c is not null && SchoolDays.Contains(c.Day) && lessonIds.Contains(c.PeriodId))
            .Select(c =>
            {
                var odd = Known(c.SubjectId);
                var even = c.AlternatesWeekly ? Known(c.EvenWeekSubjectId) : null;
                var alternates = c.AlternatesWeekly && odd != even;
                var note = string.IsNullOrWhiteSpace(c.Note) ? null : Truncate(c.Note.Trim(), ScheduleCell.MaxNoteLength);
                return c with { SubjectId = odd, AlternatesWeekly = alternates, EvenWeekSubjectId = alternates ? even : null, Note = note };
            })
            .Where(c => !c.IsEmpty)
            .Reverse()
            .DistinctBy(c => (c.Day, c.PeriodId))
            .Reverse()
            .ToList();

        // A row removed in the settings takes its adjustments along; calendar lessons are kept while the link is away.
        var adjustments = (Adjustments ?? [])
            .Where(a => a is not null && (a.PeriodId is { } row ? a.LessonKey is null && lessonIds.Contains(row) : !string.IsNullOrWhiteSpace(a.LessonKey)))
            .Select(a => a with
            {
                Note = string.IsNullOrWhiteSpace(a.Note) ? null : Truncate(a.Note.Trim(), LessonAdjustment.MaxNoteLength),
                Title = Truncate((a.Title ?? "").Trim(), LessonAdjustment.MaxTitleLength),
            })
            .Where(a => !a.IsEmpty)
            .Reverse()
            .DistinctBy(a => (a.LessonKey, a.LessonKey is null ? a.PeriodId : null, a.LessonKey is null ? a.Date : default))
            .Take(MaxAdjustments)
            .Reverse()
            .ToList();

        var level = Enum.IsDefined(Level) ? Level : SchoolLevel.PrimarySchool;
        var letter = ClassLetter?.Trim().ToUpperInvariant();
        return this with
        {
            Level = level,
            FeedUrl = string.IsNullOrWhiteSpace(FeedUrl) ? null : FeedUrl.Trim(),
            Grade = Math.Clamp(Grade, level.MinGrade(), level.MaxGrade()),
            ClassLetter = level.HasClassLetter() && letter is not null && ClassLetters.Contains(letter) ? letter : null,
            Subjects = subjects,
            Periods = periods,
            Cells = cells,
            Adjustments = adjustments,
        };
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}

/// <summary>Every schedule in the household – <c>skole/skemaer.json</c>.</summary>
public sealed record SchoolData
{
    public const int MaxSchedules = 12;

    public static SchoolData Empty { get; } = new();

    public IReadOnlyList<SchoolSchedule> Schedules { get; init; } = [];

    public SchoolData Normalized() => this with
    {
        Schedules = [.. (Schedules ?? []).Where(s => s is not null).DistinctBy(s => s.Id).Take(MaxSchedules).Select(s => s.Normalized())],
    };
}
