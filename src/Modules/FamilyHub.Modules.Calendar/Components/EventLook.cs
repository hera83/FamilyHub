namespace FamilyHub.Modules.Calendar.Components;

/// <summary>How an <see cref="EventCard"/> is drawn.</summary>
public enum EventLook
{
    /// <summary>Card in a list (week view, widget): time, title, owner and place.</summary>
    Agenda,

    /// <summary>Block in the day view's time grid – fills the height it is given.</summary>
    Block,

    /// <summary>Pill in the all-day row.</summary>
    AllDay,
}
