# Sådan bygges en ny menu (modul)

Hver menu er sit eget projekt under `src/Modules/`. Modulet beskriver sig selv med en `HubModule`-klasse –
så dukker det op i navigationen, på forsiden (widgets) og får sine egne services. Skallen skal ikke ændres.
Kalender og Madplan er skeletter, du kan kopiere fra.

## 1. Opret projektet

```powershell
dotnet new razorclasslib -n FamilyHub.Modules.Opgaver -o src/Modules/FamilyHub.Modules.Opgaver
```

Erstat projektfilen og `_Imports.razor` med indholdet fra `FamilyHub.Modules.Calendar`, og slet
skabelonens eksempelfiler (`Component1.*`, `ExampleJsInterop.cs`, `wwwroot/*`).

## 2. Modul-klassen

```csharp
public sealed class ChoresModule : HubModule
{
    public const string Path = "/opgaver";

    public override string Id => "opgaver";                    // små bogstaver, bruges til datamappe
    public override string Title => "Opgaver";                 // ét kort ord i navigationen
    public override string Description => "Hvem gør hvad i dag"; // genvej på forsiden
    public override IconName Icon => IconName.CheckSquare;
    public override string Route => Path;
    public override int Order => 30;                           // Kalender 10, Skole 20, Madplan 30 …

    public override IReadOnlyList<DashboardWidget> Widgets =>
        [DashboardWidget.For<TodayChoresWidget>(WidgetSize.Medium, order: 30)];

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ChoreService>();
    }
}
```

`ModuleCatalogBuilder` afviser ved opstart ugyldige id'er, dubletter og ruter, der kolliderer med skallen.

## 3. Startsiden

```razor
@attribute [Route(ChoresModule.Path)]
@inject ChoreService Chores
@inject IHubClock Clock

<HubPage Title="Opgaver" Subtitle="@DanishFormat.LongDate(Clock.Today)">
    …
</HubPage>
```

Brug `@attribute [Route(...)]` med modulets konstant, så ruten og navigationen altid passer sammen.

## 4. Registrér modulet

1. `src/FamilyHub.Web/FamilyHub.Web.csproj`: tilføj `<ProjectReference>` til modulet.
2. `src/FamilyHub.Web/Program.cs`: `.Add<ChoresModule>()` i `AddFamilyHubModules`.
3. `FamilyHub.slnx`: tilføj projektet under `/src/Modules/`.

## 5. Data

| Behov | Løsning |
|---|---|
| Nogle få indstillinger | `JsonFileStore<T>` i `paths.GetFilePath("opgaver/indstillinger.json")` – atomisk og strømsvigt-sikker. |
| Rigtige data (lister, historik) | EF Core + SQLite: én `DbContext` pr. modul, databasen i `paths.GetDirectory("opgaver")`. Brug `IDbContextFactory` i komponenter (kortlivede contexts i Blazor Server). |
| Familiemedlemmer | `IHouseholdService.Current.Members` – gem kun `FamilyMember.Id` i modulets data. |
| Tilstand delt mellem skærme | Singleton-service med `event Action? Changed`. Komponenter abonnerer i `OnInitialized`, afmelder i `Dispose` og opdaterer med `InvokeAsync(StateHasChanged)`. |

## 6. Widget på forsiden

- Et `HubCard` med titel, ikon og `Href` til modulet – hele kortet fører videre.
- Læsbart på afstand: 1–5 linjer, det vigtigste stort. Ingen knapper indeni.
- `WidgetSize`: `Small`/`Medium` (én kolonne), `Large` (to kolonner), `Full` (hele bredden).
- Alle kort i en række får samme højde som det højeste. Widgetten har derfor ét rodelement: `HubCard` –
  eller en wrapper med `display: grid`, der giver højden videre til kortet (se Dexcoms `GlucoseWidget`).
- Har widget'en intet at sige lige nu, renderer den ingenting – så fylder den heller ikke på forsiden.
  Eksempel: Madplanens »I aften« vises kun kl. 06–19, og kun når der er en ret.

### Lille værdi på pauseskærmen (valgfrit)

Et modul kan vise én lille værdi i en ring ved siden af uret på pauseskærmen (fx Dexcoms blodsukker):

```csharp
public override IReadOnlyList<ScreenSaverItem> ScreenSaverItems =>
    [ScreenSaverItem.For<GlucoseSaverBubble>(order: 30)];
```

- Pauseskærmen tegner ringen og flytter den rundt – altid på den side af uret, hvor der er plads
  (`ScreenSaverLayout.BubblePosition`). Komponenten tegner kun indholdet.
- Stille som uret: kun `--hub-saver-text`/`--hub-saver-text-muted`, ingen statusfarver, ingen blink.
- Størrelser i `em` – ringen er `2.5em` bred i `--hub-saver-bubble`. Et tal i `0.85em` og en lille linje under.
- Har den intet at vise, renderer den ingenting – så forsvinder ringen også.

## 7. Regler for livscyklus og tid

- Ingen prerendering: `OnInitialized(Async)` kører én gang; JS-interop først i `OnAfterRenderAsync(firstRender)`.
- Tid: brug `IHubClock` (aldrig `DateTime.Now`). `SubscribeMinuteTick` til ure og "om 5 min".
- Baggrundsarbejde (fx kalender-synkronisering): `BackgroundService` registreret i `ConfigureServices`.
  Status vises i UI med en `InfoBox` – aldrig som toasts.
- Hemmeligheder (fx Google-nøgler) må ikke ligge i `appsettings.json` i git. Under udvikling: `dotnet user-secrets`.
  På Pi'en: en fil i datamappen eller miljøvariabler i servicen.

## 8. Tests og tjekliste

- Logik i services, dækket af tests i `tests/FamilyHub.Tests`.
- Gennemgå tjeklisten i [README.md](README.md).
