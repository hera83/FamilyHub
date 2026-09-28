using System.Text.Json;
using FamilyHub.Core.Household;
using FamilyHub.Core.Time;
using FamilyHub.Modules.Calendar.Google;
using FamilyHub.Modules.Calendar.Model;
using FamilyHub.Modules.Calendar.Services;

namespace FamilyHub.Tests.Calendar;

public class GoogleMappingTests
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneResolver.Find("Europe/Copenhagen");

    private static GoogleEventDto Event(string json) =>
        JsonSerializer.Deserialize<GoogleEventDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void Timed_events_are_converted_to_the_household_time_zone()
    {
        var e = GoogleMapping.ToEvent(Event("""
            {"id":"a","summary":" Tandlæge ","location":"Hovedgaden 12",
             "start":{"dateTime":"2026-09-28T07:00:00Z"},"end":{"dateTime":"2026-09-28T08:00:00Z"}}
            """), "cal", Copenhagen)!;

        Assert.Equal("Tandlæge", e.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.FromHours(2)), e.Start);
        Assert.Equal(TimeSpan.FromHours(2), e.Start.Offset);
        Assert.False(e.IsAllDay);
        Assert.Equal("Hovedgaden 12", e.Location);
    }

    [Fact]
    public void All_day_events_keep_their_dates()
    {
        var e = GoogleMapping.ToEvent(Event("""
            {"id":"b","summary":"Efterårsferie","start":{"date":"2026-10-12"},"end":{"date":"2026-10-17"}}
            """), "cal", Copenhagen)!;

        Assert.True(e.IsAllDay);
        Assert.Equal(new DateOnly(2026, 10, 12), e.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 17), e.EndDate);
    }

    [Theory]
    [InlineData("""{"id":"c","status":"cancelled","start":{"date":"2026-10-12"},"end":{"date":"2026-10-13"}}""")]
    [InlineData("""{"id":"d","eventType":"workingLocation","start":{"date":"2026-10-12"},"end":{"date":"2026-10-13"}}""")]
    [InlineData("""{"id":"e","summary":"Uden tid"}""")]
    public void Non_appointments_are_skipped(string json)
    {
        Assert.Null(GoogleMapping.ToEvent(Event(json), "cal", Copenhagen));
    }

    [Fact]
    public void Events_without_a_title_show_as_busy()
    {
        var e = GoogleMapping.ToEvent(Event("""{"id":"f","start":{"date":"2026-10-12"},"end":{"date":"2026-10-13"}}"""), "cal", Copenhagen)!;

        Assert.Equal("Optaget", e.Title);
    }

    [Fact]
    public void Html_descriptions_become_plain_text()
    {
        Assert.Equal("Husk kortet.\n\nParkering bag & ved siden af.",
            GoogleMapping.PlainText("<p>Husk <b>kortet</b>.</p><br><br><br>Parkering bag &amp; ved siden af."));
        Assert.Null(GoogleMapping.PlainText("<br/>  "));
    }

    [Fact]
    public void New_timed_events_are_sent_with_the_local_offset_also_across_daylight_saving()
    {
        var body = Body(new NewCalendarEvent
        {
            CalendarId = "cal",
            Title = " Fodbold ",
            Date = new DateOnly(2026, 10, 24),
            StartTime = new TimeOnly(23, 30),
            Duration = TimeSpan.FromHours(4),
        });

        // Summer time ends at 03:00 on 25 October (the clock goes back to 02:00): 4 hours later is 02:30 winter time.
        Assert.Equal("Fodbold", body.GetProperty("summary").GetString());
        Assert.Equal("2026-10-24T23:30:00+02:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("2026-10-25T02:30:00+01:00", body.GetProperty("end").GetProperty("dateTime").GetString());
    }

    [Fact]
    public void New_all_day_events_use_an_exclusive_end_date()
    {
        var body = Body(new NewCalendarEvent
        {
            CalendarId = "cal",
            Title = "Weekend",
            Date = new DateOnly(2026, 10, 3),
            IsAllDay = true,
            Days = 2,
        });

        Assert.Equal("2026-10-03", body.GetProperty("start").GetProperty("date").GetString());
        Assert.Equal("2026-10-05", body.GetProperty("end").GetProperty("date").GetString());
    }

    private static JsonElement Body(NewCalendarEvent e) =>
        JsonDocument.Parse(JsonSerializer.Serialize(GoogleMapping.ToInsertBody(e, Copenhagen))).RootElement;

    [Fact]
    public void Calendars_know_whether_they_can_get_new_events()
    {
        var writer = GoogleMapping.ToSource(new GoogleCalendarDto("emma@group", "Emma", null, "writer", false, true, false), "acc")!;
        var reader = GoogleMapping.ToSource(new GoogleCalendarDto("holidays", "Helligdage", "Helligdage i Danmark", "reader", false, true, true), "acc")!;

        Assert.True(writer.CanWrite);
        Assert.True(writer.SelectedInGoogle);
        Assert.False(reader.CanWrite);
        Assert.False(reader.SelectedInGoogle); // hidden in Google
        Assert.Equal("Helligdage i Danmark", reader.Name);
    }

    [Theory]
    [InlineData("""{"installed":{"client_id":"id-1","client_secret":"s-1","redirect_uris":["http://localhost"]}}""", "id-1")]
    [InlineData("""{"web":{"client_id":"id-2","client_secret":"s-2"}}""", "id-2")]
    [InlineData("""{"client_id":"id-3","client_secret":"s-3"}""", "id-3")]
    public void The_key_file_is_read_as_google_downloads_it(string json, string clientId)
    {
        Assert.Equal(clientId, GoogleCredentialsProvider.Parse(json)?.ClientId);
    }

    [Fact]
    public void A_key_file_without_secret_is_ignored()
    {
        Assert.Null(GoogleCredentialsProvider.Parse("""{"installed":{"client_id":"id"}}"""));
    }
}

public class CalendarMatchingTests
{
    private static readonly FamilyMember Heine = new() { Name = "Heine", Color = MemberColor.Sky };
    private static readonly FamilyMember Charlotte = new() { Name = "Charlotte", Color = MemberColor.Rose };
    private static readonly FamilyMember Emma = new() { Name = "Emma Marie", Color = MemberColor.Sand };
    private static readonly IReadOnlyList<FamilyMember> Family = [Heine, Charlotte, Emma];

    private static CalendarSource Source(string name, string id = "x@group.calendar.google.com", bool primary = false) =>
        new() { Id = id, AccountId = "acc", Name = name, IsPrimary = primary };

    [Theory]
    [InlineData("Emma", "Emma Marie")]
    [InlineData("Emmas fodbold", "Emma Marie")]
    [InlineData("charlotte", "Charlotte")]
    public void Calendars_named_after_a_family_member_belong_to_them(string name, string expected)
    {
        Assert.Equal(expected, CalendarMatching.GuessMember(Source(name), Family)?.Name);
    }

    [Fact]
    public void The_primary_calendar_matches_on_the_email_address()
    {
        Assert.Equal(Heine, CalendarMatching.GuessMember(Source("heine.ramskov2020@gmail.com", "heine.ramskov2020@gmail.com", primary: true), Family));
    }

    [Theory]
    [InlineData("Familie")]
    [InlineData("Heine og Charlotte")]
    public void Shared_or_ambiguous_calendars_belong_to_nobody(string name)
    {
        Assert.Null(CalendarMatching.GuessMember(Source(name), Family));
    }

    [Fact]
    public void Shared_calendars_get_a_colour_nobody_uses()
    {
        var used = new[] { new CalendarPreference { CalendarId = "a", Color = MemberColor.Sage } };

        Assert.Equal(MemberColor.Terracotta, CalendarMatching.NextSharedColor(Family, used));
    }
}
