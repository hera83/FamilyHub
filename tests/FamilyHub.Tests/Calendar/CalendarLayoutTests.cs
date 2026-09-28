using FamilyHub.Modules.Calendar.Model;
using FamilyHub.Modules.Calendar.Services;

namespace FamilyHub.Tests.Calendar;

public class CalendarLayoutTests
{
    private static readonly TimeSpan Summer = TimeSpan.FromHours(2);
    private static readonly DateOnly Saturday = new(2026, 9, 26);

    internal static CalendarEvent Timed(string title, DateOnly day, string from, string to, int endDayOffset = 0, string calendarId = "cal") => new()
    {
        Id = title,
        CalendarId = calendarId,
        Title = title,
        Start = new DateTimeOffset(day.ToDateTime(TimeOnly.Parse(from)), Summer),
        End = new DateTimeOffset(day.AddDays(endDayOffset).ToDateTime(TimeOnly.Parse(to)), Summer),
    };

    internal static CalendarEvent AllDay(string title, DateOnly day, int days = 1, string calendarId = "cal") => new()
    {
        Id = title,
        CalendarId = calendarId,
        Title = title,
        IsAllDay = true,
        Start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Summer),
        End = new DateTimeOffset(day.AddDays(days).ToDateTime(TimeOnly.MinValue), Summer),
    };

    [Fact]
    public void Weeks_start_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 9, 21), CalendarLayout.WeekStart(Saturday));
        Assert.Equal(new DateOnly(2026, 9, 21), CalendarLayout.WeekStart(new DateOnly(2026, 9, 21)));
        Assert.Equal(new DateOnly(2026, 9, 21), CalendarLayout.WeekStart(new DateOnly(2026, 9, 27)));
        Assert.Equal(7, CalendarLayout.WeekDays(Saturday).Count);
    }

    [Fact]
    public void Month_grid_covers_whole_weeks_around_the_month()
    {
        var weeks = CalendarLayout.MonthWeeks(Saturday);

        Assert.Equal(5, weeks.Count);
        Assert.Equal(new DateOnly(2026, 8, 31), weeks[0][0]);
        Assert.Equal(new DateOnly(2026, 10, 4), weeks[^1][6]);
        Assert.Equal((new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 5)), CalendarLayout.Range(CalendarView.Month, Saturday));
    }

    [Fact]
    public void Stepping_moves_a_day_a_week_or_a_month()
    {
        Assert.Equal(new DateOnly(2026, 9, 27), CalendarLayout.Shift(CalendarView.Day, Saturday, 1));
        Assert.Equal(new DateOnly(2026, 9, 19), CalendarLayout.Shift(CalendarView.Week, Saturday, -1));
        Assert.Equal(new DateOnly(2026, 10, 1), CalendarLayout.Shift(CalendarView.Month, new DateOnly(2026, 9, 30), 1));
    }

    [Fact]
    public void Period_titles_are_short_and_danish()
    {
        Assert.Equal("Lørdag 26. september", CalendarLayout.PeriodTitle(CalendarView.Day, Saturday, Saturday));
        Assert.Equal("I dag", CalendarLayout.PeriodDetail(CalendarView.Day, Saturday, Saturday));
        Assert.Equal("Uge 39", CalendarLayout.PeriodTitle(CalendarView.Week, Saturday, Saturday));
        Assert.Equal("21.–27. september", CalendarLayout.PeriodDetail(CalendarView.Week, Saturday, Saturday));
        Assert.Equal("September 2026", CalendarLayout.PeriodTitle(CalendarView.Month, Saturday, Saturday));
    }

    [Fact]
    public void Overlapping_events_share_the_width_and_others_get_it_all()
    {
        var events = new[]
        {
            Timed("Frokost", Saturday, "12:00", "13:00"),
            Timed("Møde", Saturday, "12:30", "14:00"),
            Timed("Kaffe", Saturday, "13:00", "13:30"),
            Timed("Fodbold", Saturday, "16:00", "17:30"),
        };

        var laidOut = CalendarLayout.LayoutDay(events, Saturday).ToDictionary(p => p.Event.Title);

        Assert.Equal((0, 2), (laidOut["Frokost"].Lane, laidOut["Frokost"].Lanes));
        Assert.Equal((1, 2), (laidOut["Møde"].Lane, laidOut["Møde"].Lanes));
        Assert.Equal((0, 2), (laidOut["Kaffe"].Lane, laidOut["Kaffe"].Lanes)); // reuses the lane Frokost freed
        Assert.Equal((0, 1), (laidOut["Fodbold"].Lane, laidOut["Fodbold"].Lanes));
        Assert.Equal(16 * 60, laidOut["Fodbold"].StartMinute);
    }

    [Fact]
    public void Short_events_do_not_cover_each_other()
    {
        var events = new[] { Timed("A", Saturday, "10:00", "10:10"), Timed("B", Saturday, "10:15", "10:30") };

        var laidOut = CalendarLayout.LayoutDay(events, Saturday);

        Assert.All(laidOut, p => Assert.Equal(2, p.Lanes));
    }

    [Fact]
    public void Hour_range_widens_to_fit_early_and_late_events()
    {
        var early = CalendarLayout.LayoutDay([Timed("Løb", Saturday, "05:30", "06:15"), Timed("Fest", Saturday, "20:00", "23:30")], Saturday);

        Assert.Equal((5, 24), CalendarLayout.HourRange(early));
        Assert.Equal((7, 21), CalendarLayout.HourRange([]));
    }

    [Fact]
    public void Events_across_midnight_show_on_both_days()
    {
        var trip = Timed("Nattog", Saturday, "20:00", "10:00", endDayOffset: 1);
        var sunday = Saturday.AddDays(1);

        Assert.True(trip.OccursOn(Saturday));
        Assert.True(trip.OccursOn(sunday));
        Assert.False(trip.OccursOn(sunday.AddDays(1)));
        Assert.Equal("Fra 20:00", CalendarLayout.TimeLabel(trip, Saturday));
        Assert.Equal("Til 10:00", CalendarLayout.TimeLabel(trip, sunday));
        Assert.Equal((0, 600), CalendarLayout.MinutesOn(trip, sunday));
    }

    [Fact]
    public void The_middle_day_of_a_long_timed_event_is_all_day()
    {
        var conference = Timed("Konference", Saturday, "09:00", "15:00", endDayOffset: 2);

        Assert.False(CalendarLayout.IsAllDayOn(conference, Saturday));
        Assert.True(CalendarLayout.IsAllDayOn(conference, Saturday.AddDays(1)));
        Assert.Equal("Hele dagen", CalendarLayout.TimeLabel(conference, Saturday.AddDays(1)));
        Assert.Empty(CalendarLayout.LayoutDay([conference], Saturday.AddDays(1)));
    }

    [Fact]
    public void An_event_ending_at_midnight_stays_on_its_day()
    {
        var evening = Timed("Fest", Saturday, "20:00", "00:00", endDayOffset: 1);

        Assert.False(evening.OccursOn(Saturday.AddDays(1)));
        Assert.Equal("20:00–00:00", CalendarLayout.TimeLabel(evening, Saturday));
    }

    [Fact]
    public void All_day_events_use_an_exclusive_end_date()
    {
        var weekend = AllDay("Sommerhus", Saturday, days: 2);

        Assert.True(weekend.OccursOn(Saturday));
        Assert.True(weekend.OccursOn(Saturday.AddDays(1)));
        Assert.False(weekend.OccursOn(Saturday.AddDays(2)));
        Assert.Equal("Lørdag 26. september – søndag 27. september", CalendarLayout.WhenLabel(weekend));
        Assert.Equal("Lørdag 26. september · Hele dagen", CalendarLayout.WhenLabel(AllDay("Fri", Saturday)));
    }

    [Fact]
    public void When_label_for_timed_events()
    {
        Assert.Equal("Lørdag 26. september · 10:00–11:30", CalendarLayout.WhenLabel(Timed("Kamp", Saturday, "10:00", "11:30")));
        Assert.Equal("Lørdag 26. september 20:00 – søndag 27. september 10:00",
            CalendarLayout.WhenLabel(Timed("Nattog", Saturday, "20:00", "10:00", endDayOffset: 1)));
    }
}
