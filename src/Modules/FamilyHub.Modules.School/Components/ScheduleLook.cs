using FamilyHub.Core.Household;
using FamilyHub.Modules.School.Schedules;
using FamilyHub.UI.Components;

namespace FamilyHub.Modules.School.Components;

/// <summary>How a schedule is named and coloured – after the child it belongs to.</summary>
public static class ScheduleLook
{
    /// <summary>Shown if the family member has been removed under Indstillinger → Familien.</summary>
    public const string UnknownName = "Uden navn";

    public static FamilyMember? Member(SchoolSchedule schedule, HouseholdSettings household) => household.FindMember(schedule.MemberId);

    public static string Name(SchoolSchedule schedule, HouseholdSettings household) => Member(schedule, household)?.Name ?? UnknownName;

    public static MemberColor Color(SchoolSchedule schedule, HouseholdSettings household) => Member(schedule, household)?.Color ?? MemberColor.Stone;

    /// <summary>"Skema for Emma".</summary>
    public static string Title(SchoolSchedule schedule, HouseholdSettings household) => $"Skema for {Name(schedule, household)}";

    /// <summary>In the same order as the family members – schedules without a member last.</summary>
    public static IReadOnlyList<SchoolSchedule> Ordered(IEnumerable<SchoolSchedule> schedules, HouseholdSettings household)
    {
        var order = household.Members.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        return [.. schedules.OrderBy(s => order.GetValueOrDefault(s.MemberId, int.MaxValue))];
    }

    /// <summary>The subject's colour as a CSS variable for <c>style="--st-color: …"</c>.</summary>
    public static string ColorVariable(SchoolSubject subject) => MemberColors.CssVariable(subject.Color);
}
