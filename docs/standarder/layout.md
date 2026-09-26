# Layout

## Skærmens opbygning

```
┌──────────┬───────────────────────────────────────────────┐
│  08:42   │  HubPage                                      │
│  fre 25.9│  Sidetitel                       [ Handling ] │
│          │  Undertitel                                   │
│  Hjem    │                                               │
│  Kalender│  HubSection                                   │
│  Madplan │  ┌─────────┐ ┌─────────┐ ┌─────────┐          │
│          │  │ HubCard │ │ HubCard │ │ HubCard │          │
│          │  └─────────┘ └─────────┘ └─────────┘          │
│ Indstill.│                                               │
├──────────┴───────────────────────────────────────────────┤
│  Skærmtastatur – kun når der skrives                     │
└──────────────────────────────────────────────────────────┘
```

- **Navigationsskinnen** til venstre: lille ur, Hjem, modulerne i rækkefølge efter `Order`,
  Indstillinger nederst. På telefoner bliver den til en bundmenu.
- **Skærmtastaturet** ligger fast i bunden (ikke oven på siden). Siden bliver kortere, og feltet,
  der skrives i, rulles automatisk frem.
- **Toasts** vises øverst i midten. **Dialoger** midt på – eller lige over tastaturet, når det er åbent.
- Kun indholdsområdet ruller – lodret. Ingen vandret rulning og ingen rulleområder inde i hinanden
  (undtagen i dialoger).

## Sidens skelet

```razor
@attribute [Route(MealPlanModule.Path)]

<HubPage Title="Madplan" Subtitle="Uge 39">
    <Actions>
        <HubButton Variant="ButtonVariant.Primary" Icon="IconName.Plus" OnClick="AddDishAsync">Tilføj ret</HubButton>
    </Actions>
    <ChildContent>
        <HubSection Title="I aften">…</HubSection>
        <HubSection Title="Resten af ugen">…</HubSection>
    </ChildContent>
</HubPage>
```

| Parameter | Regel |
|---|---|
| `Title` | Ét til tre ord. Bruges også i browserfanen. |
| `Subtitle` | Kontekst: uge, antal, status. Kan udelades. |
| `Actions` | Højst to knapper. Den primære til sidst (længst til højre). |
| `Width` | `Normal` (lister, oversigter), `Narrow` (formularer, indstillinger), `Full` (kalender, ugeplan). |
| `BackHref` / `BackLabel` | Kun på undersider, fx `BackHref="madplan" BackLabel="Madplan"`. |

## Gitter og afstande

- Kort lægges i `<HubGrid MinItemWidth="20rem">`: så mange kolonner, som der er plads til.
- Afstande følger et 4 px-gitter: `--hub-space-1` (4 px) til `--hub-space-16` (64 px).
  - Mellem sektioner: 40 px (sker automatisk i `HubPage`).
  - Mellem kort: 24 px. Inde i kort: 24 px luft.
  - `hub-stack` (lodret stak, 20 px) og `hub-row` (vandret række, 12 px, ombryder) til enkle layouts.
- Sider er højst 88rem brede (Narrow: 56rem) og centreres – så linjerne ikke bliver for lange på den store skærm.

## Typografi

Skrifttypen er **Inter** og følger med appen, så den virker uden internet.

| Token | Størrelse | Brug |
|---|---|---|
| `--hub-text-display` | 80–136 px | Uret på forsiden |
| `--hub-text-2xl` | 34 px | Sidetitel (`h1`) |
| `--hub-text-xl` | 26 px | Sektionstitel (`h2`), dialogtitel |
| `--hub-text-lg` | 21 px | Korttitel (`h3`) |
| `--hub-text-md` | 18 px | Brødtekst – standard |
| `--hub-text-sm` | 16 px | Hjælpetekst, labels, detaljer |
| `--hub-text-xs` | 14 px | Kun korte etiketter (navigation, mærkater) |

Vægte: 400 til tekst, 500 til labels, 600 til titler. Tal, der ændrer sig (ure, antal), bruger
`hub-numeric`, så cifrene står stille. Hjælpeklasser: `hub-muted` (dæmpet), `hub-subtle` (endnu mere dæmpet), `hub-small`.

## Farver

- **Flader:** varm, lys baggrund (`--hub-bg`), hvide kort (`--hub-surface`), tynde varme kanter (`--hub-border`).
- **Accent (salviegrøn, `--hub-accent`):** kun til den primære knap, valgt tilstand og aktivt menupunkt.
  Bruges sparsomt – så betyder den noget.
- **Status:** `--hub-info`, `--hub-success`, `--hub-warning`, `--hub-danger` (+ `-soft`-varianter til baggrunde).
  Afdæmpede og altid sammen med ikon og tekst.
- **Familiemedlemmer:** faste farver `--hub-person-{sage|sky|terracotta|sand|plum|rose|teal|stone}`.
  Brug `MemberColors.CssVariable(member.Color)` eller `<HubAvatar Member="…" />`.
- **Nattilstand:** alle tokens har en mørk udgave. Afprøv altid begge temaer.

## Touch-mål

| Token | Størrelse | Brug |
|---|---|---|
| `--hub-touch-min` | 48 px | Minimum for alt, der kan trykkes på |
| `--hub-control-sm` | 44 px | Kun små sekundære knapper med luft omkring (fx i et korthoved) |
| `--hub-control-md` | 56 px | Standard for knapper og felter |
| `--hub-control-lg` | 64 px | Primære handlinger på store flader |

Mindst 8 px mellem trykmål. Hele kortet eller hele rækken er trykmålet – ikke kun teksten.
Undgå bevægelser (swipe, langt tryk) som eneste måde at gøre noget på; de er usynlige.

## Bevægelse

Korte, rolige overgange (120–320 ms, `--hub-duration-*`). Ingen hop, blink eller løbende animationer.
Indstillingen "reduceret bevægelse" respekteres automatisk.

## Ikoner

Kun ikoner fra `IconName` (24×24, tynd streg). Ikoner står ved siden af tekst, ikke i stedet for –
undtagen velkendte handlinger (luk, redigér, slet, plus), og de skal have `AriaLabel`.
Nye ikoner tilføjes i `IconName.cs` + `IconPaths.cs` i samme stil.
