# Modul: Madplan

**Status:** ugens aftensmad og »I aften« på forsiden er bygget (2026-09-29). Forret og dessert (2026-10-01). Indkøbsliste og antal personer mangler.

Madplanen handler **kun om aftensmad**: pr. dag en **hovedret** og, hvis man vil, en **forret** og en **dessert** (højst én af hver). Andre måltider planlægges ikke her.

## Sådan virker den

| Handling | Hvordan |
|---|---|
| Se ugen | Mandag–søndag som kolonner, ligesom kalenderens uge. I dag er markeret; tidligere dage er dæmpede. |
| Skift uge | Pilene / »Denne uge« – eller **swipe** til siden på ugen. Ugen står i adressen (`?dato=2026-10-05`). |
| Vælg ret | Tryk på en tom dag → vælgeren: ret-type (Hovedret forvalgt), hele opskriftsbogen, kategori-chips (»Alle« forvalgt) og søgning. |
| En ret mere | Det lille stiplede **»+ Tilføj«** under dagens retter (kun når en ret-type er ledig, og ikke på tidligere dage). Foreslår dessert, derefter forret – optagne ret-typer er nedtonede. Vælges Dessert/Forret, starter listen på kategorien af samme navn, hvis bogen har den (`MealPlanRules.CategoryFor`). |
| Visning | Menurækkefølge: forret, hovedret, dessert. Hovedretten stort med billede; forret og dessert som små kort med ret-typen øverst. |
| Egen ret | Skriv i søgefeltet → »Brug »Rester««. Gemmes kun i madplanen, ikke i opskriftsbogen. |
| Se retten | Tryk på en ret → billede, tid, personer, ingredienser og noter. |
| Skift ret | »Skift ret« i dialogen → vælgeren for samme ret-type (den nuværende ret er markeret »Valgt«). |
| Flyt dag | **Hold fingeren** på en ret og træk til en anden dag (med mus: bare træk). **Hele dagens menu** (forret, hovedret, dessert) flytter med. Har måldagen retter, bytter de to dage menu. |
| Fjern ret | »Fjern« i dialogen – med »Fortryd« i toasten. |
| Print opskrift | »Print« nederst til venstre i dialogen (kun opskrifter fra bogen, og kun når printeren er sat op). Opskriften laves som PDF med billede, ingredienser og fremgangsmåde og sendes til familiens printer. Se `docs/printer.md`. |
| »I aften« på forsiden | Dagens hovedret (ellers første ret) stort, forret/dessert som linjer under – fra kl. 06:00 til 19:00 (så har vi spist). Uden ret – eller uden for tidsrummet – vises intet kort. Tider: `MealPlanRules.TonightFrom/TonightUntil`. |

Swipe og træk er genveje. Alt kan også gøres med tryk alene (standarderne: ingen skjulte fagter som eneste vej).

## Filer

| Fil | Indhold |
|---|---|
| `Pages/MealPlanPage.razor` | Siden: værktøjslinje, status (InfoBox), ugen og de to dialoger |
| `Components/MealPlanWeek.razor` (+ `.razor.js`) | De syv dage. JS-modulet står for swipe og træk-og-slip af dagens menu (`.mp-menu`) (pointer events – finger og mus) |
| `Components/RecipePickerDialog.razor` | Vælg ret: søgning, kategori-chips, egen ret |
| `Components/DinnerDialog.razor` | En planlagt ret: vis den, skift, fjern, print |
| `Print/RecipePdf.cs` + `Print/RecipePrinter.cs` | Opskriften som A4-PDF (QuestPDF) og afsendelse til printeren (`PrintService` i Core) |
| `Components/TonightWidget.razor` | »I aften« på forsiden |
| `Components/DinnerLook.cs` | Titel, ikon og faktalinje (»35 min · Aftensmad«) |
| `Plan/MealPlanService.cs` | Familiens plan: planlæg og fjern/fortryd pr. ret, flyt/byt hele dage, `Changed` til alle skærme. `MealPlanRules`: uger, flyt-reglen, ledige ret-typer og forslag |
| `Plan/DinnerCourse.cs` | Ret-typerne (`Main` = 0, `Starter`, `Dessert`), navne og menurækkefølge |
| `Plan/MealPlanDbContext.cs` + `Plan/Migrations/` | SQLite: `madplan/madplan.db` i datamappen. Tabellen `Dinners` (dato + ret-type → opskrift-id eller egen tekst). Migrationen `Courses` gør gamle retter til hovedretter |
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

Byg videre efter [docs/standarder/nyt-modul.md](../../../docs/standarder/nyt-modul.md).
