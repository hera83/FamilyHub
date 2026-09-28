using System.Globalization;
using Bunit;
using FamilyHub.Core.Household;
using FamilyHub.Modules.Calendar.Components;
using FamilyHub.Modules.Calendar.Model;
using FamilyHub.UI.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FamilyHub.Tests.Calendar;

public class CalendarComponentTests : BunitContext
{
    private static readonly DateOnly Saturday = new(2026, 9, 26);

    public CalendarComponentTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CalendarEntry Entry(string id, FamilyMember? member) =>
        new(new CalendarSource { Id = id, AccountId = "acc", Name = member?.Name ?? "Familie", CanWrite = true },
            new CalendarPreference { CalendarId = id, Color = MemberColor.Sage }, member);

    [Fact]
    public void Pressable_is_a_button_or_a_link_and_reports_taps()
    {
        var taps = 0;
        var button = Render<HubPressable>(p => p.Add(x => x.OnClick, (MouseEventArgs _) => taps++).AddChildContent("<span>Fodbold</span>"));
        var link = Render<HubPressable>(p => p.Add(x => x.Href, "kalender?visning=dag").Add(x => x.AriaLabel, "Vis dagen"));

        button.Find("button.hub-pressable").Click();

        Assert.Equal(1, taps);
        Assert.Equal("button", button.Find("button").GetAttribute("type"));
        Assert.Equal("Vis dagen", link.Find("a.hub-pressable").GetAttribute("aria-label"));
    }

    [Fact]
    public void Week_view_shows_seven_days_with_owners_and_reports_the_tapped_event()
    {
        var emma = new FamilyMember { Name = "Emma", Color = MemberColor.Sand };
        var football = CalendarLayoutTests.Timed("Fodbold", Saturday, "10:00", "11:00", calendarId: "emma");
        var weekend = CalendarLayoutTests.AllDay("Sommerhus", Saturday, days: 2, calendarId: "family");
        CalendarEvent? selected = null;

        var cut = Render<WeekView>(p => p
            .Add(x => x.Days, [.. Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 9, 21).AddDays(i))])
            .Add(x => x.Events, [football, weekend])
            .Add(x => x.Calendars, new Dictionary<string, CalendarEntry> { ["emma"] = Entry("emma", emma), ["family"] = Entry("family", null) })
            .Add(x => x.Now, new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(2)))
            .Add(x => x.OnSelect, (CalendarEvent e) => selected = e));

        Assert.Equal(7, cut.FindAll(".cal-week__day").Count);
        Assert.Contains("cal-week__day--today", cut.FindAll(".cal-week__day")[5].ClassName);
        Assert.Equal(2, cut.FindAll(".cal-event--allday").Count); // Saturday and Sunday
        Assert.Contains("Emma", cut.Find(".cal-event--agenda .cal-event__meta").TextContent);
        Assert.Single(cut.FindAll(".cal-now")); // "Nu" before the 10:00 football

        cut.Find("button[aria-label^='Fodbold']").Click();

        Assert.Same(football, selected);
    }
}
