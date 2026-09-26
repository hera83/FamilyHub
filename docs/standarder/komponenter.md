# Komponenter

Alle komponenter ligger i `FamilyHub.UI` (namespace `FamilyHub.UI.Components`) og hedder `Hub…`.
Moduler bruger kun disse. Se dem live under **Indstillinger → Komponentoversigt**
(`src/FamilyHub.Web/Components/Pages/Settings/DesignGuide.razor` er også et godt kodeeksempel).

## Layout

| Komponent | Brug |
|---|---|
| `HubPage` | Rammen om hver side: titel, undertitel, handlinger, tilbage-link, bredde. |
| `HubSection` | En gruppe indhold med titel, beskrivelse og evt. handlinger. |
| `HubGrid` | Responsivt gitter til kort (`MinItemWidth`, `Gap`). |

## Visning

| Komponent | Brug |
|---|---|
| `HubCard` | Informationskort. `Title`, `Subtitle`, `Icon`, `HeaderActions`, `Footer`. `Variant`: Default, Subtle, Accent. Med `OnClick`/`Href` er hele kortet et trykmål (så ingen knapper indeni). |
| `HubList` + `HubListItem` | Lister med rækker på mindst 64 px. `Leading` (fx avatar), `Title`, `Subtitle`, `Trailing` (værdi, mærkat, stille knapper). Med `OnClick`/`Href` er hele rækken et trykmål. |
| `HubAvatar` | Initialer i familiemedlemmets farve: `<HubAvatar Member="member" />`. |
| `HubBadge` | Lille status eller antal: `<HubBadge Tone="HubTone.Success">I dag</HubBadge>`. |
| `EmptyState` | Når der ikke er noget at vise endnu – ikon, titel, én sætning, én handling. `Compact` i kort. |
| `HubSkeleton` / `HubSpinner` | Indlæsning. Skeleton i lister og kort; spinner kun i små områder. |
| `Icon` | `<Icon Name="IconName.Calendar" />`. `Label` kun hvis ikonet står alene. |

## Feedback

| Komponent / service | Brug |
|---|---|
| `IToastService` | Korte beskeder: `Toasts.Success("Gemt")`, `Toasts.Undoable("Mælk er fjernet", UndoAsync)`. |
| `InfoBox` | Varig tilstand på siden. `Tone`, `Title`, tekst, `Actions`, evt. `OnDismiss`. |
| `HubDialog` | Kort opgave i en dialog: `<HubDialog @bind-Open="open" Title="…">…<Footer>…</Footer></HubDialog>`. |
| `DialogService` | Bekræftelse: `if (await Dialogs.ConfirmAsync(new() { … })) { … }`. |

Se [feedback.md](feedback.md) for hvornår hvad bruges.

## Input

| Komponent | Brug |
|---|---|
| `HubButton` | Den eneste knap. `Variant` (Primary, Secondary, Quiet, Danger), `Size`, `Icon`, `Href`, `Block`. Async `OnClick` giver automatisk travl-tilstand og fejl-toast. Kun ikon ⇒ `AriaLabel` påkrævet. |
| `NumberStepper<T>` | − værdi + for små tal. `Min`, `Max`, `Step`, `Unit`, `Format`. Hold for at tælle hurtigt. Virker med `int`, `decimal`, `double`. |
| `HubChoice<T>` | Vælg én af få synlige muligheder. `Segmented` (2–5 korte) eller `Chips` (flere/længere). |
| `HubSwitch` | Til/fra med øjeblikkelig virkning. Hele rækken er trykmålet. |
| `MemberColorPicker` | Farve til et familiemedlem. |
| `HubTextField` | Fri tekst. `Kind` bestemmer tastaturet. `OnCommit` til auto-gem, `OnEnter`, `EnterKey`, `EnterLabel`, `KeepKeyboardOnEnter`, `Error`, `Help`, `MaxLength`. Virker med `EditForm`-validering via `@bind-Value`. |

Se [input-og-tastatur.md](input-og-tastatur.md) for hvornår hvad bruges.

## Eksempler

### Kort på forsiden (widget)

```razor
<HubCard Title="I aften" Subtitle="Tirsdag" Icon="IconName.Utensils" Href="madplan">
    <p class="hub-card__title">Lasagne</p>
    <p class="hub-muted">4 personer · Charlotte laver mad</p>
</HubCard>
```

### Liste med handlinger

```razor
<HubList>
    @foreach (var item in items)
    {
        <HubListItem Title="@item.Name" Subtitle="@item.Note">
            <Leading><HubAvatar Member="@item.Owner" Size="AvatarSize.Small" /></Leading>
            <Trailing>
                <HubButton Variant="ButtonVariant.Quiet" Icon="IconName.Trash" AriaLabel="@($"Fjern {item.Name}")" OnClick="() => RemoveAsync(item)" />
            </Trailing>
        </HubListItem>
    }
</HubList>
```

### Dialog med felt

```razor
<HubButton Variant="ButtonVariant.Primary" Icon="IconName.Plus" OnClick="() => open = true">Tilføj vare</HubButton>

<HubDialog @bind-Open="open" Title="Ny vare" CloseOnBackdrop="false">
    <ChildContent>
        <HubTextField Label="Vare" Placeholder="Fx Havregryn" AutoFocus="true" @bind-Value="name" OnEnter="AddAsync" />
        <NumberStepper Label="Antal" @bind-Value="count" Min="1" Max="20" Unit="stk." />
    </ChildContent>
    <Footer>
        <HubButton Variant="ButtonVariant.Quiet" OnClick="() => open = false">Annuller</HubButton>
        <HubButton Variant="ButtonVariant.Primary" Icon="IconName.Plus" OnClick="AddAsync">Tilføj</HubButton>
    </Footer>
</HubDialog>
```

### Stepper med eget format

```razor
<NumberStepper Label="Spisetid" @bind-Value="hour" Min="15" Max="21" Format="@(h => $"{h:00}:00")" />
<NumberStepper Label="Mængde" @bind-Value="kilos" Min="0" Max="10" Step="0.5m" Unit="kg" />
```

## CSS i moduler

- Brug komponenterne og hjælpeklasserne først (`hub-stack`, `hub-row`, `hub-muted`, `hub-numeric` …).
- Eget udseende i et modul: komponentens `.razor.css` (scoped) – og kun med tokens: `var(--hub-space-4)`, `var(--hub-text-lg)`, `var(--hub-accent)` osv.
- Aldrig hårdkodede farver eller px-størrelser på tekst. Rem-enheder (via tokens), så "Visningsstørrelse" virker.
