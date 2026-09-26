using FamilyHub.UI.Components;

namespace FamilyHub.UI.Keyboard;

public enum KeyboardLayoutKind
{
    /// <summary>Danish letters with a number row – names, notes, search.</summary>
    Text,
    Email,
    Url,

    /// <summary>Compact number pad for whole numbers.</summary>
    Numeric,

    /// <summary>Number pad with Danish decimal comma.</summary>
    Decimal,
    Phone,
}

public enum KeyKind
{
    Character,
    Space,
    Shift,
    Backspace,
    Enter,

    /// <summary>Switches layer, e.g. letters ↔ symbols.</summary>
    Layer,
    CursorLeft,
    CursorRight,

    /// <summary>Empty space in a row.</summary>
    Spacer,
}

public sealed record KeyDefinition
{
    public required KeyKind Kind { get; init; }

    /// <summary>Text inserted by a Character key, e.g. "q" or ".dk".</summary>
    public string? Text { get; init; }

    /// <summary>Label when it differs from <see cref="Text"/>.</summary>
    public string? Label { get; init; }

    public IconName? Icon { get; init; }

    /// <summary>Relative width – 1 is a normal letter key.</summary>
    public double Width { get; init; } = 1;

    public string? TargetLayer { get; init; }

    public string? AriaLabel { get; init; }

    /// <summary>Single letters follow shift/caps lock.</summary>
    public bool IsLetter => Kind == KeyKind.Character && Text is { Length: 1 } text && char.IsLetter(text[0]);
}

public sealed record KeyboardLayer(string Name, IReadOnlyList<IReadOnlyList<KeyDefinition>> Rows);

public sealed record KeyboardLayout(KeyboardLayoutKind Kind, IReadOnlyList<KeyboardLayer> Layers, bool IsCompact);

/// <summary>
/// Key layouts. Full layouts have 5 rows of 11 units, so keys line up across rows and
/// switching between letters and symbols never changes the keyboard's height.
/// </summary>
public static class KeyboardLayouts
{
    public const string Letters = "letters";
    public const string Symbols = "symbols";
    public const string Digits = "digits";

    private static readonly KeyboardLayout TextLayout = Full(KeyboardLayoutKind.Text, C(","), Space(3), C("."));
    private static readonly KeyboardLayout EmailLayout = Full(KeyboardLayoutKind.Email, C("@"), Space(2), C("."), C("-"));
    private static readonly KeyboardLayout UrlLayout = Full(KeyboardLayoutKind.Url, C("/"), C("-"), C(".dk", 2), C("."));
    private static readonly KeyboardLayout NumericLayout = Pad(KeyboardLayoutKind.Numeric, new KeyDefinition { Kind = KeyKind.Spacer });
    private static readonly KeyboardLayout DecimalLayout = Pad(KeyboardLayoutKind.Decimal, C(","));
    private static readonly KeyboardLayout PhoneLayout = Pad(KeyboardLayoutKind.Phone, C("+"));

    public static KeyboardLayout Get(KeyboardLayoutKind kind) => kind switch
    {
        KeyboardLayoutKind.Email => EmailLayout,
        KeyboardLayoutKind.Url => UrlLayout,
        KeyboardLayoutKind.Numeric => NumericLayout,
        KeyboardLayoutKind.Decimal => DecimalLayout,
        KeyboardLayoutKind.Phone => PhoneLayout,
        _ => TextLayout,
    };

    private static KeyboardLayout Full(KeyboardLayoutKind kind, params KeyDefinition[] bottomMiddle)
    {
        const string numberRow = "1 2 3 4 5 6 7 8 9 0 -";

        var letters = new KeyboardLayer(Letters,
        [
            Chars(numberRow),
            Chars("q w e r t y u i o p å"),
            Chars("a s d f g h j k l æ ø"),
            [Shift(), .. Chars("z x c v b n m"), Backspace(2)],
            BottomRow(Layer(Symbols, "123", "Tal og tegn"), bottomMiddle),
        ]);

        var symbols = new KeyboardLayer(Symbols,
        [
            Chars(numberRow),
            Chars("! ? \" ' ( ) & @ + = %"),
            Chars("/ * # _ € $ ½ ° : ; ~"),
            [.. Chars("< > [ ] { } | \\ ^"), Backspace(2)],
            BottomRow(Layer(Letters, "ABC", "Bogstaver"), bottomMiddle),
        ]);

        return new KeyboardLayout(kind, [letters, symbols], IsCompact: false);
    }

    private static KeyDefinition[] BottomRow(KeyDefinition layerKey, KeyDefinition[] middle) =>
        [layerKey, CursorLeft(), .. middle, CursorRight(), Enter(2)];

    private static KeyboardLayout Pad(KeyboardLayoutKind kind, KeyDefinition bottomLeft)
    {
        var digits = new KeyboardLayer(Digits,
        [
            [C("1"), C("2"), C("3"), Backspace(1)],
            [C("4"), C("5"), C("6"), CursorLeft()],
            [C("7"), C("8"), C("9"), CursorRight()],
            [bottomLeft, C("0"), Enter(2)],
        ]);

        return new KeyboardLayout(kind, [digits], IsCompact: true);
    }

    private static KeyDefinition C(string text, double width = 1) => new() { Kind = KeyKind.Character, Text = text, Width = width };

    private static KeyDefinition[] Chars(string keys) => [.. keys.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(k => C(k))];

    private static KeyDefinition Space(double width) => new() { Kind = KeyKind.Space, Text = " ", Width = width, AriaLabel = "Mellemrum" };

    private static KeyDefinition Shift() => new() { Kind = KeyKind.Shift, Icon = IconName.Shift, Width = 2, AriaLabel = "Store bogstaver" };

    private static KeyDefinition Backspace(double width) => new() { Kind = KeyKind.Backspace, Icon = IconName.Backspace, Width = width, AriaLabel = "Slet" };

    private static KeyDefinition Enter(double width) => new() { Kind = KeyKind.Enter, Width = width, AriaLabel = "Enter" };

    private static KeyDefinition Layer(string target, string label, string ariaLabel) =>
        new() { Kind = KeyKind.Layer, TargetLayer = target, Label = label, Width = 2, AriaLabel = ariaLabel };

    private static KeyDefinition CursorLeft() => new() { Kind = KeyKind.CursorLeft, Icon = IconName.ChevronLeft, AriaLabel = "Flyt markøren til venstre" };

    private static KeyDefinition CursorRight() => new() { Kind = KeyKind.CursorRight, Icon = IconName.ChevronRight, AriaLabel = "Flyt markøren til højre" };
}
