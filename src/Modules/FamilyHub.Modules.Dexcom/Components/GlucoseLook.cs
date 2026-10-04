using FamilyHub.Modules.Dexcom.Glucose;
using FamilyHub.UI.Components;

namespace FamilyHub.Modules.Dexcom.Components;

/// <summary>How a level is shown – the same on the Dexcom page and the home screen card. Pure functions – unit-tested.</summary>
public static class GlucoseLook
{
    /// <summary>The CSS modifier: "low", "in-range", "high" – "missing" when there is no current reading.</summary>
    public static string CssName(GlucoseLevel? level) => level switch
    {
        GlucoseLevel.Low => "low",
        GlucoseLevel.High => "high",
        GlucoseLevel.InRange => "in-range",
        _ => "missing",
    };

    /// <summary>The icon next to "Lav", "Høj" and "I målområdet" – so colour never stands alone.</summary>
    public static IconName Icon(GlucoseLevel level) => level switch
    {
        GlucoseLevel.Low => IconName.AlertCircle,
        GlucoseLevel.High => IconName.AlertTriangle,
        _ => IconName.CheckCircle,
    };
}
