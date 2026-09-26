using FamilyHub.UI.Keyboard;

namespace FamilyHub.Tests.UI;

public class KeyboardTests
{
    public static TheoryData<KeyboardLayoutKind> AllKinds => [.. Enum.GetValues<KeyboardLayoutKind>()];

    public static TheoryData<KeyboardLayoutKind> FullKinds => [KeyboardLayoutKind.Text, KeyboardLayoutKind.Email, KeyboardLayoutKind.Url];

    [Fact]
    public void Text_layout_is_danish()
    {
        var letters = KeyboardLayouts.Get(KeyboardLayoutKind.Text).Layers.Single(l => l.Name == KeyboardLayouts.Letters);
        var texts = letters.Rows.SelectMany(r => r).Select(k => k.Text).ToHashSet();

        Assert.Contains("æ", texts);
        Assert.Contains("ø", texts);
        Assert.Contains("å", texts);
        Assert.Equal(["q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "å"], letters.Rows[1].Select(k => k.Text));
    }

    [Theory]
    [MemberData(nameof(FullKinds))]
    public void Full_layouts_have_rows_of_eleven_units_so_keys_line_up(KeyboardLayoutKind kind)
    {
        foreach (var layer in KeyboardLayouts.Get(kind).Layers)
        {
            Assert.Equal(5, layer.Rows.Count);
            Assert.All(layer.Rows, row => Assert.Equal(11, row.Sum(k => k.Width)));
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_layer_has_exactly_one_enter_and_a_backspace(KeyboardLayoutKind kind)
    {
        foreach (var layer in KeyboardLayouts.Get(kind).Layers)
        {
            var keys = layer.Rows.SelectMany(r => r).ToList();
            Assert.Single(keys, k => k.Kind == KeyKind.Enter);
            Assert.Contains(keys, k => k.Kind == KeyKind.Backspace);
        }
    }

    [Theory]
    [InlineData(KeyboardLayoutKind.Numeric, null)]
    [InlineData(KeyboardLayoutKind.Decimal, ",")]
    [InlineData(KeyboardLayoutKind.Phone, "+")]
    public void Number_pads_are_compact_with_all_digits(KeyboardLayoutKind kind, string? extraKey)
    {
        var layout = KeyboardLayouts.Get(kind);
        var texts = layout.Layers.Single().Rows.SelectMany(r => r).Select(k => k.Text).ToHashSet();

        Assert.True(layout.IsCompact);
        Assert.All(Enumerable.Range(0, 10), digit => Assert.Contains(digit.ToString(), texts));
        if (extraKey is not null)
        {
            Assert.Contains(extraKey, texts);
        }
    }

    [Fact]
    public void Only_single_letters_follow_shift()
    {
        var keys = KeyboardLayouts.Get(KeyboardLayoutKind.Url).Layers.SelectMany(l => l.Rows).SelectMany(r => r).ToList();

        Assert.True(keys.Single(k => k.Text == "q").IsLetter);
        Assert.False(keys.First(k => k.Text == ".dk").IsLetter); // on both layers
        Assert.False(keys.First(k => k.Text == "1").IsLetter);
    }

    [Theory]
    [InlineData("numeric", KeyboardLayoutKind.Numeric)]
    [InlineData("decimal", KeyboardLayoutKind.Decimal)]
    [InlineData("phone", KeyboardLayoutKind.Phone)]
    [InlineData("email", KeyboardLayoutKind.Email)]
    [InlineData("url", KeyboardLayoutKind.Url)]
    [InlineData("text", KeyboardLayoutKind.Text)]
    [InlineData("something-new", KeyboardLayoutKind.Text)]
    [InlineData(null, KeyboardLayoutKind.Text)]
    public void Field_info_maps_to_layout(string? layout, KeyboardLayoutKind expected)
    {
        Assert.Equal(expected, KeyboardTarget.From(new KeyboardFieldInfo { Layout = layout }).Layout);
    }

    [Theory]
    [InlineData(null, false, null, "Færdig")]
    [InlineData("done", false, null, "Færdig")]
    [InlineData("next", false, null, "Næste")]
    [InlineData("search", false, null, "Søg")]
    [InlineData("go", false, null, "Gå")]
    [InlineData("send", false, null, "Send")]
    [InlineData("enter", false, null, "")]
    [InlineData(null, true, null, "")]
    [InlineData("done", false, "Tilføj", "Tilføj")]
    public void Enter_key_gets_a_danish_label(string? hint, bool multiline, string? custom, string expected)
    {
        var target = KeyboardTarget.From(new KeyboardFieldInfo { EnterKeyHint = hint, Multiline = multiline, EnterLabel = custom });

        Assert.Equal(expected, target.EnterLabel);
    }

    [Fact]
    public void Keyboard_state_changes_raise_events_and_ignore_invalid_transitions()
    {
        var keyboard = new KeyboardService();
        var changes = 0;
        keyboard.Changed += () => changes++;

        keyboard.Minimize(); // nothing to minimise yet
        Assert.Equal(KeyboardVisibility.Hidden, keyboard.Visibility);

        keyboard.Attach(new KeyboardTarget(KeyboardLayoutKind.Text, false, "Færdig", "Navn"));
        keyboard.Minimize();
        Assert.Equal(KeyboardVisibility.Minimized, keyboard.Visibility);

        keyboard.Expand();
        Assert.True(keyboard.IsExpanded);

        keyboard.Detach();
        keyboard.Detach(); // already hidden
        Assert.Null(keyboard.Target);
        Assert.Equal(4, changes);
    }
}
