# Opskrifter fra Mambeno

Family Hub kan læse Mambenos opskrifter fra familiens eget API foran Mambeno (`https://mambeno.appcore.cc`).
API'et henter selv opskrifterne fra mambeno.dk og holder dem friske. Family Hub læser kun – der kan ikke oprettes,
rettes eller slettes.

Koden ligger i `src/FamilyHub.Core/Mambeno/` og bruges via `IMambenoService`, så alle menuer kan bruge den.
Tests: `tests/FamilyHub.Tests/Core/MambenoServiceTests.cs`.

**Bruges i dag af madplanen:** »Inspiration« i vælgeren til en ret. En valgt Mambeno-opskrift kopieres til familiens
opskriftsbog, så den kan rettes der – se madplanens README (`src/Modules/FamilyHub.Modules.MealPlan/README.md`).

## Opsætning

| Miljø | Adresse | Nøgle |
|---|---|---|
| Udvikling | `appsettings.json` (`FamilyHub:Mambeno:BaseUrl`) | `dotnet user-secrets set "FamilyHub:Mambeno:ApiKey" "ak_…" --project src/FamilyHub.Web` |
| Server (Docker) | `MAMBENO_API_URL` i `.env` | `MAMBENO_API_KEY` i `.env` |

Brug en almindelig nøgle (`ak_…`) oprettet til Family Hub med `POST /Keys/Create`, aldrig API'ets master key.
Mangler nøglen, starter appen alligevel. `IsConfigured` er så falsk, og alle kald giver `MambenoError.NotConfigured`.

## Kald

| Metode | API | Svar |
|---|---|---|
| `GetCategoriesAsync()` | `GET /api/v1/categories` | Alle kategorier med `RecipeCount` og `ParentId` (træ) |
| `GetCategoryAsync(id)` | `GET /api/v1/categories/{id}` | Én kategori, `null` hvis den ikke findes |
| `SearchRecipesAsync(MambenoQuery)` | `GET /api/v1/recipes` | Én side (`MambenoRecipePage`: `Items`, `TotalCount`, `TotalPages`, `HasMore`) |
| `GetRecipeAsync(id)` | `GET /api/v1/recipes/{id}` | Én opskrift, `null` hvis den ikke findes |
| `GetStatusAsync()` | `GET /Health` | `MambenoStatus`. 503 er et normalt svar: data er ældre end 7 dage eller aldrig hentet |

**`MambenoQuery`**: `Search`, `Category` (præcist navn), `CategoryId`, `MaxTotalMinutes`, `Ingredients` (alle skal
indgå i et ingrediensnavn), `ModifiedSince`, `Sort` (`Title` A–Å, `LastModified` nyeste først, `TotalTime` hurtigste
først), `Page` (fra 1), `PageSize` (1–200, standard 50). Alle filtre skal være opfyldt.

Fejl kommer som `MambenoException` med en kort dansk besked, klar til en toast eller `InfoBox`:
`NotConfigured`, `Offline`, `Unauthorized`, `Invalid` (400) eller `Failed`.
`/Keys` og `/Log` kræver master key og bruges ikke af Family Hub.

## Modellen

**`MambenoRecipe`**: `Id`, `Title`, `Description`, `CategoryId?` + `Category` (hovedkategori), `Categories` (alle),
`PrepTimeMinutes`, `CookTimeMinutes`, `TotalTimeMinutes`, `Servings`, `Author`, `Notes`, `LastModified` (UTC),
`ImageUrl?`, `SourceUrl?` (opskriften på mambeno.dk), `Ingredients`, `Steps`, og hjælperne `HasTime`, `CanScale`, `HasImage`.

**`MambenoIngredient`**: `Amount` (tekst som skrevet, fx "0.5"), `Quantity?` (tal), `Unit`, `Name`, `Group?`
(fx "Dressing"), `LinkedRecipeId?` (Mambenos egen opskrift på ingrediensen, fx "færdige muffins").

## Data, som de ser ud i virkeligheden

Målt 2026-10-05:

- **Mange opskrifter:** 4.837 opskrifter og 47 kategorier. En side med 200 opskrifter fylder ca. 750 KB.
  Hent derfor side for side, og hent ikke hele bogen ved hvert kald.
- **Kategorier er et træ:** "Asiatisk", "Fisk", "Burgere" … ligger under "Aftensmad" (`ParentId = 18`). Nogle er tomme
  (`RecipeCount = 0`, fx "Billigste opskrifter"). En opskrift ligger ofte i flere kategorier.
- **Ingen billeder:** `ImageUrl` er tom på alle opskrifter. Lav et pænt layout uden billede.
- **Ingen sværhedsgrad:** Mambeno angiver den ikke, så API'ets `difficulty`-filter giver altid en tom liste. Den er
  udeladt i servicen.
- **Grupper i stedet for overskrifter:** ingredienserne har `Group` (fx "Tilbehør", "Sovs"). Vis grupperne som små
  overskrifter, når en opskrift har flere.
- **Tider og personer er næsten altid udfyldt.** Få opskrifter har `TotalTimeMinutes = 0` eller `Servings = 0`.
  0 betyder "ukendt". Vis så ingen tid, og skalér ikke.
- **Mængder** kommer med punktum ("0.5"). Brug `Quantity` og `DanishFormat`, når de vises.
- **Kategori-ikoner** er alle `"cookie"` og bruges ikke. Family Hub bruger egne ikoner (`IconName`).
- **Tekstsøgning** i API'et ignorerer kun store og små bogstaver for a–z, ikke æøå.
