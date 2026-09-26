using System.Globalization;
using Bunit;
using FamilyHub.Core.Notifications;
using FamilyHub.UI.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHub.Tests.UI;

public class ComponentTests : BunitContext
{
    private readonly ToastService toasts = new(new FakeTimeProvider(), NullLogger<ToastService>.Instance);

    public ComponentTests()
    {
        // The app runs in Danish (Program.cs); make the tests independent of the machine's language.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("da-DK");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IToastService>(toasts);
    }

    [Theory]
    [InlineData(InputKind.Text, "text", "text", "sentences", "done")]
    [InlineData(InputKind.Name, "text", "text", "words", "done")]
    [InlineData(InputKind.Search, "search", "text", "none", "search")]
    [InlineData(InputKind.Email, "email", "email", "none", "done")]
    [InlineData(InputKind.Number, "numeric", "numeric", "none", "done")]
    [InlineData(InputKind.Decimal, "decimal", "decimal", "none", "done")]
    [InlineData(InputKind.Phone, "tel", "phone", "none", "done")]
    public void Text_field_kind_sets_what_the_keyboard_reads(InputKind kind, string inputMode, string layout, string autoCapitalize, string enterKeyHint)
    {
        var cut = Render<HubTextField>(p => p.Add(x => x.Kind, kind).Add(x => x.Label, "Felt"));
        var input = cut.Find("input");

        Assert.Equal(inputMode, input.GetAttribute("inputmode"));
        Assert.Equal(layout, input.GetAttribute("data-osk-layout"));
        Assert.Equal(autoCapitalize, input.GetAttribute("autocapitalize"));
        Assert.Equal(enterKeyHint, input.GetAttribute("enterkeyhint"));
        Assert.Equal("false", input.GetAttribute("spellcheck"));
        Assert.Equal(input.Id, cut.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void Multiline_field_is_a_textarea_where_enter_makes_a_new_line()
    {
        var cut = Render<HubTextField>(p => p.Add(x => x.Kind, InputKind.Multiline));

        Assert.Equal("enter", cut.Find("textarea").GetAttribute("enterkeyhint"));
    }

    [Fact]
    public void Text_field_updates_while_typing_and_commits_once()
    {
        var values = new List<string>();
        var commits = new List<string>();
        var cut = Render<HubTextField>(p => p
            .Add(x => x.ValueChanged, (string v) => values.Add(v))
            .Add(x => x.OnCommit, (string v) => commits.Add(v)));
        var input = cut.Find("input");

        input.Input("Mæ");
        input.Input("Mælk");
        input.Change("Mælk");
        input.Blur();

        Assert.Equal(["Mæ", "Mælk"], values);
        Assert.Equal(["Mælk"], commits);
    }

    [Fact]
    public void Enter_raises_OnEnter_and_custom_enter_label_is_exposed_to_the_keyboard()
    {
        var entered = 0;
        var cut = Render<HubTextField>(p => p
            .Add(x => x.EnterLabel, "Tilføj")
            .Add(x => x.KeepKeyboardOnEnter, true)
            .Add(x => x.OnEnter, () => entered++));
        var input = cut.Find("input");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(1, entered);
        Assert.Equal("Tilføj", input.GetAttribute("data-osk-enter-label"));
        Assert.Equal("keep", input.GetAttribute("data-osk-enter"));
    }

    [Fact]
    public void Error_text_marks_the_field_invalid()
    {
        var cut = Render<HubTextField>(p => p.Add(x => x.Error, "Skriv et navn"));

        Assert.Contains("hub-field--invalid", cut.Find(".hub-field").ClassName);
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Contains("Skriv et navn", cut.Find(".hub-field__error").TextContent);
    }

    [Fact]
    public void Stepper_stops_at_its_limits()
    {
        var value = 11;
        var cut = Render<NumberStepper<int>>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, 1)
            .Add(x => x.Max, 12)
            .Add(x => x.ValueChanged, (int v) => value = v));

        cut.Find("button[aria-label='Flere']").PointerDown(new PointerEventArgs { PointerType = "touch" });
        Assert.Equal(12, value);
        Assert.True(cut.Find("button[aria-label='Flere']").HasAttribute("disabled"));
        Assert.False(cut.Find("button[aria-label='Færre']").HasAttribute("disabled"));
    }

    [Fact]
    public void Stepper_keyboard_activation_steps_once_and_decimals_use_danish_comma()
    {
        var value = 1.5m;
        var cut = Render<NumberStepper<decimal>>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Step, 0.5m)
            .Add(x => x.Unit, "kg")
            .Add(x => x.ValueChanged, (decimal v) => value = v));

        cut.Find("button[aria-label='Færre']").Click(new MouseEventArgs { Detail = 0 });

        Assert.Equal(1.0m, value);
        Assert.Contains("1,0", cut.Find(".hub-stepper__value").TextContent);
    }

    [Fact]
    public void Button_turns_an_exception_into_an_error_toast()
    {
        var cut = Render<HubButton>(p => p
            .Add(x => x.Text, "Gem")
            .Add(x => x.ErrorMessage, "Kunne ikke gemme")
            .Add(x => x.OnClick, () => throw new InvalidOperationException("boom")));

        cut.Find("button").Click();

        var toast = Assert.Single(toasts.Visible);
        Assert.Equal(ToastLevel.Error, toast.Level);
        Assert.Equal("Kunne ikke gemme", toast.Title);
        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Button_is_disabled_while_its_action_runs()
    {
        var release = new TaskCompletionSource();
        var cut = Render<HubButton>(p => p.Add(x => x.Text, "Gem").Add(x => x.OnClick, () => release.Task));

        var click = cut.Find("button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.True(cut.Find("button").HasAttribute("disabled")));

        release.SetResult();
        await click;
        cut.WaitForAssertion(() => Assert.False(cut.Find("button").HasAttribute("disabled")));
    }

    [Fact]
    public void Icon_only_button_gets_square_style()
    {
        var cut = Render<HubButton>(p => p.Add(x => x.Icon, IconName.Edit).Add(x => x.AriaLabel, "Redigér"));

        Assert.Contains("hub-btn--icon", cut.Find("button").ClassName);
        Assert.Equal("Redigér", cut.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void Toast_host_shows_toasts_from_the_service()
    {
        var cut = Render<ToastHost>();

        toasts.Success("Gemt");
        toasts.Error("Kunne ikke hente", "Prøv igen");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".hub-toast").Count));
        Assert.Equal("alert", cut.Find(".hub-toast--error").GetAttribute("role"));
        Assert.Contains("Prøv igen", cut.Find(".hub-toast--error").TextContent);
    }

    [Fact]
    public void Danger_info_box_is_announced_as_an_alert()
    {
        var cut = Render<InfoBox>(p => p.Add(x => x.Tone, HubTone.Danger).Add(x => x.Title, "Madplanen kunne ikke hentes"));

        Assert.Equal("alert", cut.Find(".hub-infobox").GetAttribute("role"));
        Assert.Contains("hub-infobox--danger", cut.Find(".hub-infobox").ClassName);
    }

    [Fact]
    public void Choice_marks_the_selected_option_and_reports_changes()
    {
        var selected = "b";
        var cut = Render<HubChoice<string>>(p => p
            .Add(x => x.Options, [new ChoiceOption<string>("a", "A"), new ChoiceOption<string>("b", "B")])
            .Add(x => x.Value, selected)
            .Add(x => x.ValueChanged, (string v) => selected = v));

        Assert.Equal("true", cut.FindAll("[role=radio]")[1].GetAttribute("aria-checked"));

        cut.FindAll("[role=radio]")[0].Click();
        Assert.Equal("a", selected);
    }
}
