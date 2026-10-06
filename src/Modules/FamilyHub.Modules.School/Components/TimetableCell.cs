using FamilyHub.Modules.School.Schedules;

namespace FamilyHub.Modules.School.Components;

/// <summary>A tapped lesson in the timetable: which day and which row.</summary>
public sealed record TimetableCell(DayOfWeek Day, SchoolPeriod Period);
