using FamilyHub.Modules.School.Feeds;
using FamilyHub.Modules.School.Schedules;

namespace FamilyHub.Tests.School;

public sealed class LessonDetailsTests
{
    /// <summary>A description as AAU's Moodle writes it (after unescaping).</summary>
    private const string Moodle =
        "Jura 4 (Samlet hold)\n\nCOURSE\n3. semester / Modul 5 - Jura - E26 [1]\n\nTEACHER\nAnette Jul Mortensen (anettejm@law.aau.dk)\n\n \n\n" +
        "Links:\n------\n[1] https://www.moodle.aau.dk/local/planning/eventcourselink.php?c=59574&amp;e=255643";

    private static IEnumerable<(string? Label, string Lines)> Read(string? description) =>
        LessonDetails.Sections(description).Select(s => (s.Label, string.Join(" | ", s.Lines)));

    [Fact]
    public void A_moodle_description_gets_danish_headings_and_no_links_or_email()
    {
        Assert.Equal(
            [(null, "Jura 4 (Samlet hold)"), ("Kursus", "3. semester / Modul 5 - Jura - E26"), ("Underviser", "Anette Jul Mortensen")],
            Read(Moodle));
    }

    [Fact]
    public void Several_teachers_are_undervisere()
    {
        var sections = LessonDetails.Sections("TEACHER\nAnne Hansen (ah@math.aau.dk)\nBo Berg (bb@math.aau.dk)");

        var teachers = Assert.Single(sections);
        Assert.Equal("Undervisere", teachers.Label);
        Assert.Equal(["Anne Hansen", "Bo Berg"], teachers.Lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \n")]
    [InlineData("Links:\n------\n[1] https://moodle.example/x\n[2] www.example.dk")]
    public void Nothing_to_show_gives_no_sections(string? description) => Assert.Empty(LessonDetails.Sections(description));

    [Fact]
    public void Other_calendars_become_paragraphs_with_their_own_lines()
    {
        Assert.Equal(
            [(null, "Husk lommeregner | og bogen"), (null, "PBL"), ("Lokaler", "B2 og B3")],
            Read("Husk lommeregner\nog bogen\n\nPBL\n\nLOKALER\nB2 og B3\n\nTeams: <https://teams.example/møde>"));
    }

    [Fact]
    public void A_cell_with_several_lessons_shows_them_all_and_a_cancelled_one_keeps_its_details()
    {
        var events = FeedTests.BusyAutumn();
        events.Add(new FeedEvent(new DateTime(2026, 10, 5, 8, 30, 0), new DateTime(2026, 10, 5, 9, 30, 0), "Vejledning", "Gruppe 4", "v", "TEACHER\nBo Berg"));
        var gone = new FeedEvent(new DateTime(2026, 10, 7, 8, 15, 0), new DateTime(2026, 10, 7, 10, 0, 0), "Temadag", "Aula", "t", "Om fremtiden");
        var cancelled = new FeedChange("t", gone, null, DateTimeOffset.UnixEpoch);

        var week = SchoolWeek.For(FeedTests.University(), new DateOnly(2026, 10, 5), events, [cancelled]);
        var rows = week.Schedule.Periods.Where(p => !p.IsBreak).ToList();

        Assert.Equal(["Øvelser", "Vejledning"], week.CalendarLessonsAt(DayOfWeek.Monday, rows[0]).Select(l => l.Lesson.Title));
        var wednesday = Assert.Single(week.CalendarLessonsAt(DayOfWeek.Wednesday, rows[0]));
        Assert.Equal(FeedChangeKind.Cancelled, wednesday.Change?.Kind);
        Assert.Equal("Om fremtiden", wednesday.Lesson.Description);
        Assert.Empty(week.CalendarLessonsAt(DayOfWeek.Tuesday, rows[0]));

        // A fixed timetable has nothing from a calendar.
        Assert.Empty(SchoolWeek.For(FeedTests.University() with { FeedUrl = null }, new DateOnly(2026, 10, 5), events)
            .CalendarLessonsAt(DayOfWeek.Monday, rows[0]));
    }
}
