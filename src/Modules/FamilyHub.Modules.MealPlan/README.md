# Modul: Madplan

**Status:** ugens aftensmad er bygget (2026-09-29). Indkøbsliste, antal personer og widget på forsiden mangler.

Madplanen handler **kun om aftensmad** – én ret pr. dag. Andre måltider planlægges ikke her.

## Sådan virker den

| Handling | Hvordan |
|---|---|
| Se ugen | Mandag–søndag som kolonner, ligesom kalenderens uge. I dag er markeret; tidligere dage er dæmpede. |
| Skift uge | Pilene / »Denne uge« – eller **swipe** til siden på ugen. Ugen står i adressen (`?dato=2026-10-05`). |
| Vælg ret | Tryk på en tom dag → vælgeren: hele opskriftsbogen, kategori-chips (»Alle« forvalgt) og søgning. |
| Egen ret | Skriv i søgefeltet → »Brug »Rester««. Gemmes kun i madplanen, ikke i opskriftsbogen. |
| Se retten | Tryk på en dag med en ret → billede, tid, personer, ingredienser og noter. |
| Skift ret | »Skift ret« i dialogen → vælgeren (den nuværende ret er markeret »Valgt«). |
| Flyt ret | **Hold fingeren** på retten og træk den til en anden dag (med mus: bare træk). Står der en ret, bytter de plads. Uden fagter: »Flyt til en anden dag« i dialogen. |
| Fjern ret | »Fjern« i dialogen – med »Fortryd« i toasten. |

Swipe og træk er genveje. Alt kan også gøres med tryk alene (standarderne: ingen skjulte fagter som eneste vej).

## Filer

| Fil | Indhold |
|---|---|
| `Pages/MealPlanPage.razor` | Siden: værktøjslinje, status (InfoBox), ugen og de to dialoger |
| `Components/MealPlanWeek.razor` (+ `.razor.js`) | De syv dage. JS-modulet står for swipe og træk-og-slip (pointer events – finger og mus) |
| `Components/RecipePickerDialog.razor` | Vælg ret: søgning, kategori-chips, egen ret |
| `Components/DinnerDialog.razor` | En planlagt ret: vis den, flyt, skift, fjern |
| `Components/DinnerLook.cs` | Titel, ikon og faktalinje (»35 min · Aftensmad«) |
| `Plan/MealPlanService.cs` | Familiens plan: planlæg, flyt/byt, fjern/fortryd, `Changed` til alle skærme. `MealPlanRules`: uger og flyt-reglen |
| `Plan/MealPlanDbContext.cs` + `Plan/Migrations/` | SQLite: `madplan/madplan.db` i datamappen. Tabellen `Dinners` (dato → opskrift-id eller egen tekst) |
| `Recipes/` | Opskriftsbogen – se nedenfor |

Databasen oprettes/opgraderes automatisk ved første brug (`MigrateAsync`). Ændres modellen, laves en ny migration:

```powershell
dotnet ef migrations add <Navn> --project src/Modules/FamilyHub.Modules.MealPlan --output-dir Plan/Migrations
```

## Opskrifter

Retterne kommer fra familiens opskriftsbog via dens API. Se **[docs/opskrifter-api.md](../../../docs/opskrifter-api.md)** –
den beskriver API'et, data som de faktisk ser ud, og hvad `RecipeService` kan. `RecipeSyncWorker` holder den lokale
kopi frisk (hvert 15. minut), så siderne aldrig venter på nettet.

Madplanen gemmer kun opskriftens **id** (+ titlen, så retten kan vises, selvom opskriftsbogen ikke kan nås).
Omdøbes en opskrift i bogen, viser madplanen det nye navn.

| Fil (`Recipes/`) | Indhold |
|---|---|
| `RecipeService.cs` | Singleton til brugerfladen: lokal kopi (virker offline), søgning, skrivninger, `Changed`, `Status` |
| `RecipeSyncWorker.cs` | Baggrundsjob: henter bogen ved start og hvert 15. minut (efter 2 min ved fejl) |
| `RecipeApiClient.cs` | Ét kald pr. endpoint + oversættelse af API'ets JSON (`RecipeMapping`) |
| `RecipeModels.cs` | `Recipe`, `RecipeIngredient`, `RecipeCategory`, `RecipeQuery`, `RecipeDraft` … |
| `RecipeMath.cs` | Skalering til antal personer, danske mængder (1½), samlet indkøbsliste |
| `RecipeSearch.cs` / `RecipeValidation.cs` | Lokal søgning; tjek af felter før afsendelse |
| `RecipeApiOptions.cs` / `RecipeApiException.cs` | Opsætning (adresse + nøgle) og fejl med danske beskeder |

## Næste skridt (ikke besluttet)

- Antal personer pr. ret med `NumberStepper` (skalering findes allerede i `RecipeMath`).
- Indkøbsliste ud fra ugens retter (`RecipeMath.CombineForShopping`) plus manuelle varer.
- Opskriftsvisning i køkkenet: fremgangsmåden trin for trin med stor tekst.
- Widget på forsiden: »I aften«.

Byg videre efter [docs/standarder/nyt-modul.md](../../../docs/standarder/nyt-modul.md).
