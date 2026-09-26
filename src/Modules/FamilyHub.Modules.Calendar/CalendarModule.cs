using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;

namespace FamilyHub.Modules.Calendar;

/// <summary>The family calendar – the shared Google Calendar on the kitchen screen.</summary>
public sealed class CalendarModule : HubModule
{
    public const string Path = "/kalender";

    public override string Id => "kalender";

    public override string Title => "Kalender";

    public override string Description => "Familiens fælles kalender";

    public override IconName Icon => IconName.Calendar;

    public override string Route => Path;

    public override int Order => 10;
}
