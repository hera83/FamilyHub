using FamilyHub.Core.Household;
using FamilyHub.Modules.Calendar.Model;

namespace FamilyHub.Modules.Calendar.Components;

/// <summary>A column in the day view: one family member, or the shared calendars.</summary>
public sealed record DayColumn(string Key, string Label, FamilyMember? Member, MemberColor Color, IReadOnlyList<CalendarEvent> Events);
