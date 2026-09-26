using FamilyHub.Core.Household;

namespace FamilyHub.UI.Components;

/// <summary>Colour meaning shared by InfoBox, badges and toasts.</summary>
public enum HubTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

public enum ButtonVariant
{
    /// <summary>The single most important action in a view. Max one per view.</summary>
    Primary,

    /// <summary>Other actions (default).</summary>
    Secondary,

    /// <summary>Low-emphasis actions: "Annuller", icon buttons, "Tilbage".</summary>
    Quiet,

    /// <summary>Destructive action in a confirmation dialog.</summary>
    Danger,
}

public enum ButtonSize
{
    /// <summary>44px – compact, secondary places only.</summary>
    Small,

    /// <summary>56px – default.</summary>
    Medium,

    /// <summary>64px – primary actions on large screens.</summary>
    Large,
}

public enum ButtonType
{
    Button,
    Submit,
}

public enum PageWidth
{
    /// <summary>Up to 88rem – dashboards and lists.</summary>
    Normal,

    /// <summary>Up to 56rem – forms and settings.</summary>
    Narrow,

    /// <summary>Full width – calendars and week plans.</summary>
    Full,
}

public enum DialogSize
{
    Small,
    Medium,
    Large,
}

public enum AvatarSize
{
    Small,
    Medium,
    Large,
}

public enum ChoiceAppearance
{
    /// <summary>Connected buttons – 2 to 5 short options.</summary>
    Segmented,

    /// <summary>Separate, wrapping pills – more options or longer labels.</summary>
    Chips,
}

/// <summary>What kind of text a <see cref="HubTextField"/> takes. Decides the on-screen keyboard layout.</summary>
public enum InputKind
{
    /// <summary>Free text, sentence case.</summary>
    Text,

    /// <summary>Names – capital letter on every word.</summary>
    Name,
    Search,
    Email,
    Url,
    Phone,

    /// <summary>Whole numbers – compact number pad. Prefer NumberStepper for small counts.</summary>
    Number,

    /// <summary>Decimal numbers with Danish comma – number pad.</summary>
    Decimal,

    /// <summary>Several lines – notes. Enter makes a new line.</summary>
    Multiline,
}

/// <summary>Label and behaviour of the Enter key on the on-screen keyboard.</summary>
public enum EnterKey
{
    /// <summary>Chosen from the <see cref="InputKind"/>.</summary>
    Default,

    /// <summary>"Færdig" – closes the keyboard.</summary>
    Done,

    /// <summary>"Næste" – moves to the next field.</summary>
    Next,

    /// <summary>"Søg".</summary>
    Search,

    /// <summary>"Gå".</summary>
    Go,

    /// <summary>"Send".</summary>
    Send,

    /// <summary>↵ – new line / plain Enter.</summary>
    Enter,
}

/// <summary>An option in <see cref="HubChoice{TValue}"/>.</summary>
public sealed record ChoiceOption<TValue>(TValue Value, string Label, IconName? Icon = null, bool Disabled = false);

public static class MemberColors
{
    /// <summary>CSS token suffix: <c>var(--hub-person-{name})</c>.</summary>
    public static string CssName(MemberColor color) => color.ToString().ToLowerInvariant();

    public static string CssVariable(MemberColor color) => $"var(--hub-person-{CssName(color)})";

    public static string DanishName(MemberColor color) => color switch
    {
        MemberColor.Sage => "Salvie",
        MemberColor.Sky => "Himmelblå",
        MemberColor.Terracotta => "Terrakotta",
        MemberColor.Sand => "Sand",
        MemberColor.Plum => "Blomme",
        MemberColor.Rose => "Rosa",
        MemberColor.Teal => "Petrol",
        MemberColor.Stone => "Sten",
        _ => color.ToString(),
    };
}

internal static class ToneExtensions
{
    public static string CssName(this HubTone tone) => tone.ToString().ToLowerInvariant();

    public static IconName DefaultIcon(this HubTone tone) => tone switch
    {
        HubTone.Success => IconName.CheckCircle,
        HubTone.Warning => IconName.AlertTriangle,
        HubTone.Danger => IconName.AlertCircle,
        _ => IconName.Info,
    };
}
