namespace FamilyHub.UI.Keyboard;

/// <summary>What keyboard.js reports about a focused text field.</summary>
public sealed class KeyboardFieldInfo
{
    /// <summary>text | email | url | phone | numeric | decimal.</summary>
    public string? Layout { get; init; }

    public bool Multiline { get; init; }

    /// <summary>The field's <c>enterkeyhint</c> attribute.</summary>
    public string? EnterKeyHint { get; init; }

    /// <summary>Custom Enter label from <c>data-osk-enter-label</c>, e.g. "Tilføj".</summary>
    public string? EnterLabel { get; init; }

    /// <summary>The field's label, aria-label or placeholder.</summary>
    public string? Label { get; init; }
}

/// <summary>The field the keyboard currently serves, translated into keyboard terms.</summary>
public sealed record KeyboardTarget(KeyboardLayoutKind Layout, bool Multiline, string EnterLabel, string? FieldLabel)
{
    public static KeyboardTarget From(KeyboardFieldInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var layout = (info.Layout ?? "").ToLowerInvariant() switch
        {
            "email" => KeyboardLayoutKind.Email,
            "url" => KeyboardLayoutKind.Url,
            "phone" => KeyboardLayoutKind.Phone,
            "numeric" => KeyboardLayoutKind.Numeric,
            "decimal" => KeyboardLayoutKind.Decimal,
            _ => KeyboardLayoutKind.Text,
        };

        return new KeyboardTarget(layout, info.Multiline, ResolveEnterLabel(info), string.IsNullOrWhiteSpace(info.Label) ? null : info.Label.Trim());
    }

    /// <summary>Danish label for the Enter key. Empty = show the ↵ icon.</summary>
    internal static string ResolveEnterLabel(KeyboardFieldInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.EnterLabel))
        {
            return info.EnterLabel.Trim();
        }

        if (info.Multiline)
        {
            return "";
        }

        return (info.EnterKeyHint ?? "").ToLowerInvariant() switch
        {
            "next" => "Næste",
            "previous" => "Forrige",
            "search" => "Søg",
            "go" => "Gå",
            "send" => "Send",
            "enter" => "",
            _ => "Færdig",
        };
    }
}
