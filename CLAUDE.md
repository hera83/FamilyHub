# Family Hub – projektinstruktioner til Claude

Familiens køkkenskærm: serveren kører i Docker i homelab'en, og en Raspberry Pi 5 med 19" touchskærm viser den i kiosktilstand.
Kommunikér med brugeren på dansk. Brugerfladen er på dansk.

## Kommandoer

| Formål | Kommando |
|---|---|
| Byg | `dotnet build FamilyHub.slnx` |
| Kør | `dotnet run --project src/FamilyHub.Web` → http://localhost:5080 |
| Test | `dotnet test FamilyHub.slnx` (xUnit, bUnit, WebApplicationFactory) |
| UI-røgtest | Kør appen, derefter `cd tools/ui-smoke; npm install; node smoke.mjs` – tjekker touch/tastatur og gemmer skærmbilleder, som du skal se på |
| Demodata (kalender uden Google) | `cd tools/ui-smoke; node demo-data.mjs`, start appen med `$env:FamilyHub__DataDirectory = (Resolve-Path ./demo-data)` |
| Kildepakke til serveren | `.\deploy\publish-server.ps1 -SkipDeploy` |
| Deploy til serveren (Docker) | `.\deploy\publish-server.ps1 -Server homelab.local -User <bruger>` |
| Kiosk på Pi'en | `.\deploy\opsaet-kiosk.ps1 -ServerUrl http://homelab.local:8080` |
| Google-login (tunnel) | `.\deploy\forbind-google.ps1 -Server homelab.local -User <bruger>` |

Kører appen allerede, låser den DLL'erne – stop processen på port 5080, før du bygger igen.

## Arkitektur

Blazor Server (.NET 10, interaktiv **uden prerendering**) + Chromium i kiosktilstand. Detaljer: `docs/arkitektur.md`.

```
FamilyHub.Core  ←  FamilyHub.UI  ←  FamilyHub.Modules.*  ←  FamilyHub.Web
(logik)            (designsystem,     (én menu pr. projekt)   (skal, forside,
                    tastatur, moduler)                         indstillinger)
```

## Obligatoriske regler, når du bygger brugerflade og menuer

Læs `docs/standarder/` før en ny menu bygges. Det vigtigste:

1. **Ny menu = nyt projekt** i `src/Modules/` med en `HubModule`-klasse (`docs/standarder/nyt-modul.md`).
   Skallen i `FamilyHub.Web` ændres kun for at registrere modulet (projektreference + `.Add<…>()` i `Program.cs`).
2. **Kun komponenter fra FamilyHub.UI:** `HubPage`, `HubSection`, `HubGrid`, `HubCard`, `HubList`/`HubListItem`,
   `HubButton`, `HubTextField`, `NumberStepper`, `HubChoice`, `HubSwitch`, `InfoBox`, `HubDialog`, `EmptyState`,
   `HubAvatar`, `HubBadge`, `HubPressable`, `Icon`. Ingen rå `<button>`, `<input>` eller hjemmelavede dialoger.
3. **Kun design-tokens** (`var(--hub-…)` fra `tokens.css`). Modul-CSS i `.razor.css`. Ingen hårdkodede farver
   eller px-tekststørrelser – ellers virker nattilstand og visningsstørrelse ikke.
4. **Vælg frem for at skrive.** Små tal → `NumberStepper`; få valg → `HubChoice`; til/fra → `HubSwitch`;
   kun fri tekst → `HubTextField` med korrekt `Kind`. **Ingen `AutoFocus`** – undtagen første felt i et forløb,
   brugeren selv har startet (fx dialog åbnet med "Tilføj"). Se `docs/standarder/input-og-tastatur.md`.
5. **Feedback:** synligt resultat → ingen besked; ellers `Toasts.Success`; sletning → gør det straks +
   `Toasts.Undoable` (ingen "Er du sikker?"); varig tilstand → `InfoBox`; validering → feltets `Error`;
   `Dialogs.ConfirmAsync` kun ved uigenkaldelige handlinger. Toast-varigheder styres i `ToastDefaults` – aldrig pr. modul.
6. **Fejl:** async handlinger via `HubButton` (travl-tilstand + fejl-toast) eller `Toasts.TryAsync(...)`.
7. **Tekst:** dansk, kort, verber på knapper, ingen udråbstegn; datoer/tid via `DanishFormat`. Se `docs/standarder/tekst-og-tone.md`.
8. **Tid** via `IHubClock` (aldrig `DateTime.Now`). **Data** via `IAppDataPaths` + `JsonFileStore<T>`;
   rigtige data med EF Core + SQLite, én `DbContext` pr. modul.
9. **Blazor:** JS-interop kun i `OnAfterRenderAsync`. Event-abonnementer afmeldes i `Dispose`;
   opdater UI fra andre tråde med `InvokeAsync(StateHasChanged)`. Lambdaer med én parameter må ikke hedde `_`,
   hvis du bruger discard `_ =` inde i dem.
10. **Verificér visuelt**, før du melder noget færdigt: kør appen + `tools/ui-smoke`, se på skærmbillederne,
    tjek lyst og mørkt tema. Tilføj unit-tests for ny logik.

## Kodekonventioner

- C#-navne og kommentarer på engelsk; UI-tekst og dokumentation på dansk.
- File-scoped namespaces; nullable-advarsler er fejl; pakkeversioner kun i `Directory.Packages.props`.
- Komponenter i `FamilyHub.UI` bruger `@namespace FamilyHub.UI.Components` (én flad namespace for moduler).
- Nye ikoner: `IconName.cs` + `IconPaths.cs` (24×24, streg, Feather-stil).
- C# 14: `field` er et nøgleord inde i properties – brug ikke `field` som navn på et felt.

## Vigtige filer

| Fil | Indhold |
|---|---|
| `src/FamilyHub.UI/wwwroot/css/tokens.css` | Alle farver, størrelser, afstande (lyst + mørkt tema) |
| `src/FamilyHub.UI/Keyboard/` + `wwwroot/js/keyboard.js` | Skærmtastaturet (layouts, synlighed, indtastning) |
| `src/FamilyHub.Core/Devices/KeyboardPolicy.cs` | Hvornår tastaturet må vises (Auto/Altid/Fra) |
| `src/FamilyHub.Core/Notifications/` | Toast-service og fælles regler (`ToastDefaults`) |
| `src/FamilyHub.UI/Modules/` | Modul-kontrakten (`HubModule`, `DashboardWidget`, `ModuleCatalog`) |
| `src/FamilyHub.Web/Program.cs` | Registrering af moduler |
| `src/FamilyHub.Web/Components/Pages/Settings/DesignGuide.razor` | Levende komponentoversigt – også kodeeksempel |
| `Dockerfile`, `docker-compose.yml`, `.env.example` | Serveren i Docker; indstillinger og hemmeligheder kun i `.env` (aldrig i git eller imaget) |
| `deploy/` + `docs/raspberry-pi.md` | Scripts til server og kiosk; opsætning af køkkenskærmen |
| `docs/google-kalender.md` | Google-opsætning til kalenderen (nøgler i `.env`, login via SSH-tunnel) |
| `docs/opskrifter-api.md` | Madplanens opskrifter: opskriftsbogens API, `RecipeService` og muligheder til UI'et – læs før madplanens UI |
