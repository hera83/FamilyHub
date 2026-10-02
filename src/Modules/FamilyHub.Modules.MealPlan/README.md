# Modul: Madplan

**Status:** ugens aftensmad og »I aften« på forsiden er bygget (2026-09-29). Forret og dessert (2026-10-01). Indkøbsliste (2026-10-02). Antal personer mangler.

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
| Indkøbsliste | Knappen **»Indkøbsliste«** øverst til højre → en bred dialog for ugen på skærmen. Se afsnittet nedenfor. |
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
| `Components/ShoppingListDialog.razor` | Indkøbslisten: varerne efter afdeling, faste varer, basisvarer, print |
| `Components/ShoppingLineRow.razor` | Én vare (tryk = streg ud / sæt basisvare på listen; blyant = »Sådan køber I …«) |
| `Components/GroceryRuleDialog.razor` | Sådan købes en vare: basisvare, samlet mængde eller hele pakker (pakkestørrelser), afdeling |
| `Components/FixedItemDialog.razor` | Tilføj/redigér/fjern en fast vare |
| `Shopping/` | Indkøbslistens hjælper og service – se nedenfor |
| `Print/ShoppingListPdf.cs` + `Print/ShoppingListPrinter.cs` | Indkøbslisten som A4-PDF (to kolonner, bokse at krydse af) og afsendelse til printeren |
| `Print/PrintJobToasts.cs` | Følger en dialogs udskrifter og siger til ved problemer (fælles for opskrift og indkøbsliste) |
| `Components/TonightWidget.razor` | »I aften« på forsiden |
| `Components/DinnerLook.cs` | Titel, ikon og faktalinje (»35 min · Aftensmad«) |
| `Plan/MealPlanService.cs` | Familiens plan: planlæg og fjern/fortryd pr. ret, flyt/byt hele dage, `Changed` til alle skærme. `MealPlanRules`: uger, flyt-reglen, ledige ret-typer og forslag |
| `Plan/DinnerCourse.cs` | Ret-typerne (`Main` = 0, `Starter`, `Dessert`), navne og menurækkefølge |
| `Plan/MealPlanDbContext.cs` + `Plan/Migrations/` | SQLite: `madplan/madplan.db` i datamappen. Tabellen `Dinners` (dato + ret-type → opskrift-id eller egen tekst). Migrationen `Courses` gør gamle retter til hovedretter. `ShoppingList` tilføjer `FixedItems`, `GroceryRules` og `ShoppingMarks` |
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

## Indkøbslisten

Knappen **»Indkøbsliste«** på madplanen åbner en bred dialog for ugen på skærmen. Alt gemmes med det samme og ses på alle
skærme – der er ingen »Gem«.

| Del | Sådan virker den |
|---|---|
| Skal købes (venstre) | Ugens ingredienser og de faste varer, lagt sammen pr. vare og sorteret som i butikken (frugt og grønt → brød → kød og fisk → mejeri og køl → kolonial → frost → drikkevarer → husholdning → andet). Under hver vare: hvilke retter den kommer fra, og hvad der skal bruges, når der købes hele pakker (»skal bruge 1,3 l«). |
| Streg ud | Tryk på en vare – fx hvis I har den i forvejen, eller den er købt. Gælder kun denne uge. Overstregede varer kommer ikke med på print. |
| Resten af ugen / Hele ugen | Kun i denne uge efter mandag: retter fra tidligere dage er spist og tælles ikke med (forvalgt). |
| Faste varer (højre) | Det, I køber hver uge (»Mælk 2 l«, »Skyr«, »Pålæg til madpakker 3 pakker«) – eller kun i én uge. Navnet skrives; antal, enhed og uge vælges. Enter (»Tilføj«) tilføjer og gør klar til den næste. Tryk på en fast vare for at rette eller fjerne den (med »Fortryd«). En fast vare lægges sammen med den samme vare fra retterne: 2 l fast mælk + 2 dl til pandekager = 3 l. |
| Basisvarer (højre) | Salt, peber, olie, mel, sukker, krydderier … står ikke på listen, selv om retterne bruger dem. De vises med ugens forbrug, og et tryk sætter en, der mangler, på listen (kun denne uge). |
| Blyanten | »Sådan køber I …«: basisvare til/fra, *samlet mængde* eller *hele pakker* med op til tre pakkestørrelser (− / +), og afdeling. Et eksempel viser, hvad listen vil sige. »Brug standard« glemmer familiens valg. Gælder alle uger. |
| Print | A4 i to kolonner med en boks at krydse af pr. vare (kun når printeren er sat op). |

### Hjælperen (`Shopping/`)

Rene funktioner med unit-tests (`tests/FamilyHub.Tests/MealPlan/ShoppingCalculatorTests.cs`). Selve listen gemmes
aldrig – den regnes ud hver gang, så den altid følger madplanen og opskriftsbogen.

| Fil | Indhold |
|---|---|
| `ShoppingCalculator.cs` | Lægger ugens behov sammen pr. vare og siger, hvad der skal købes. Spænd som »700-800 g« tæller som det største tal; tekst som »lidt« vises, som den er. |
| `ShoppingUnits.cs` | Enheder: rumfang i ml (ml, cl, dl, l, spsk = 15 ml, tsk = 5 ml), vægt i g (g, kg), stk – og egne enheder (dåse, glas, pakke …), som kun lægges sammen med sig selv. Skriver resultatet på dansk: »1,3 l«, »2½ dl«, »1,25 kg«, »3 dåser«. Skeer bliver skeer op til 10 spsk. |
| `PackPlanner.cs` | Hele pakker: færrest mulige pakker og mindst muligt til overs, vejet mod hinanden. 1,3 kg oksekød med pakker på 350 g, 500 g, 800 g og 1,2 kg → »800 g + 500 g«; 1,3 l mælk → »2 l«; 320 g smør → »2 × 250 g«. Lidt under er i orden (højst 5 % og en tiendedel af den mindste pakke): 1020 g mel = én pose på 1 kg. |
| `GroceryCatalog.cs` | Varekataloget: ca. 180 danske varer med afdeling, andre navne (»sødmælk«, »piskefløde«, stavemåder fra opskriftsbogen), pakkestørrelser, vægt pr. dl (så »4 dl havregryn« og »200 g havregryn« kan lægges sammen) og basisvarer. Genkender navne som »Lun mælk« (= mælk), »Æg til pensling«, »kærnemælk, A38 eller ymer« og »øko appelsiner«. Ukendte navne beholder deres eget navn; ligner det sidste/første ord en kendt vare (»røget laks«), lånes afdeling og basisvare – men ikke pakkerne. Postevand kommer aldrig på listen. |
| `ShoppingModels.cs` | `GroceryRule` (familiens måde at købe en vare på), `FixedItem`, `ShoppingLine`, `ShoppingList`, `ShoppingMarks` |
| `ShoppingListService.cs` | Singleton: bygger ugens liste (madplan + opskriftsbog + egne data) og gemmer faste varer, regler og afkrydsninger. Afkrydsninger og faste varer for én uge ryddes op 8 uger efter. |

Kød, fisk og ost står som samlet mængde, da pakkerne varierer meget. Vil I hellere have »2 × 400 g«, sættes
pakkestørrelserne med blyanten – én gang, så gælder det fremover.

Mangler en vare i kataloget, eller er en standard forkert: tilføj/ret den i `GroceryCatalog.All` (testen
`Every_name_in_the_catalogue_points_to_one_grocery` fanger dubletter). Familiens egne valg i `GroceryRules` går altid forud.

## Næste skridt (ikke besluttet)

- Antal personer pr. ret med `NumberStepper` (skalering findes allerede i `RecipeMath`) – indkøbslisten bruger i dag
  opskriftens eget antal.
- Opskriftsvisning i køkkenet: fremgangsmåden trin for trin med stor tekst.
- Indkøbslisten på telefonen i butikken (kræver adgang uden for hjemmenettet).

Byg videre efter [docs/standarder/nyt-modul.md](../../../docs/standarder/nyt-modul.md).
