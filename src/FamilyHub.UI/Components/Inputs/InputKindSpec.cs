namespace FamilyHub.UI.Components;

/// <summary>
/// The HTML attributes each <see cref="InputKind"/> needs. These attributes are what the
/// on-screen keyboard reads to pick its layout, Enter label and auto-capitalisation.
/// </summary>
internal sealed record InputKindSpec(
    string Type,
    string InputMode,
    string AutoCapitalize,
    string AutoComplete,
    string KeyboardLayout,
    string? Pattern,
    EnterKey DefaultEnterKey)
{
    public static InputKindSpec For(InputKind kind) => kind switch
    {
        InputKind.Name => new("text", "text", "words", "off", "text", null, EnterKey.Done),
        InputKind.Search => new("search", "search", "none", "off", "text", null, EnterKey.Search),
        InputKind.Email => new("text", "email", "none", "email", "email", null, EnterKey.Done),
        InputKind.Url => new("url", "url", "none", "url", "url", null, EnterKey.Go),
        InputKind.Phone => new("tel", "tel", "none", "tel", "phone", null, EnterKey.Done),
        InputKind.Number => new("text", "numeric", "none", "off", "numeric", "[0-9]*", EnterKey.Done),
        InputKind.Decimal => new("text", "decimal", "none", "off", "decimal", null, EnterKey.Done),
        InputKind.Multiline => new("textarea", "text", "sentences", "off", "text", null, EnterKey.Enter),
        _ => new("text", "text", "sentences", "off", "text", null, EnterKey.Done),
    };

    public static string? EnterKeyHint(EnterKey key) => key switch
    {
        EnterKey.Done => "done",
        EnterKey.Next => "next",
        EnterKey.Search => "search",
        EnterKey.Go => "go",
        EnterKey.Send => "send",
        EnterKey.Enter => "enter",
        _ => null,
    };
}
