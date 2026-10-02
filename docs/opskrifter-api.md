# Opskrifter: API og service (til madplanen)

Madplanen henter retterne fra familiens **opskriftsbog** ("Mors Opskrifter"). Det er en separat app med sit eget API.
Denne fil beskriver API'et, som det faktisk opfører sig, og den service Family Hub bruger til det. Læs den, før
madplanens brugerflade bygges.

Koden ligger i `src/Modules/FamilyHub.Modules.MealPlan/Recipes/`. Tests: `tests/FamilyHub.Tests/MealPlan/`.

## Opsætning

| Hvad | Hvor |
|---|---|
| Adresse | `FamilyHub:MealPlan:Recipes:BaseUrl` – standard `https://opskriftsbog.ramskov.pro/api/v1` (appsettings.json) |
| Nøgle (hemmelig) | `FamilyHub:MealPlan:Recipes:ApiKey` – sendes som headeren `X-Api-Key` |
| Server (Docker) | `RECIPES_API_KEY=` (og evt. `RECIPES_API_URL=`) i `.env` → `docker compose up -d` |
| Udvikling | `dotnet user-secrets set "FamilyHub:MealPlan:Recipes:ApiKey" "<nøgle>" --project src/FamilyHub.Web` |

Nøglen er den delte nøgle fra opskriftsbogens `Api:SharedKey`. Den må aldrig ligge i git eller i appsettings.json.
Mangler nøglen, starter appen alligevel. `RecipeService.IsConfigured` er så `false`, og madplanen skal vise en
`InfoBox`, der forklarer det.

## Tre lag – hvad bruges hvornår?

| Klasse | Levetid | Brug den til |
|---|---|---|
| `RecipeService` | Singleton | **Alt i brugerfladen.** Lokal kopi af hele bogen (hukommelse + `madplan/opskrifter.json`), lokal søgning, alle skrivninger, `Changed`-event og `Status`. Virker uden net. |
| `RecipeApiClient` | Singleton | Rå adgang, ét kald pr. endpoint. Kun hvis servicen mangler noget, fx API-søgning med sider. |
| `RecipeMath` / `RecipeSearch` / `RecipeValidation` | Statiske | Skalering til antal personer, danske mængder (1½), indkøbsliste, lokal søgning og tjek af felter før afsendelse. Rene funktioner. |

Bogen er lille (23 opskrifter, ca. 56 KB i september 2026). Derfor hentes **hele bogen** ved hver opdatering
(`GET /lookups` + `GET /recipes?pageSize=200`), og al søgning og filtrering sker lokalt. Det er hurtigt, virker
offline og fanger også opskrifter, der er slettet i bogen.

## RecipeService – kort vejledning

```csharp
@inject RecipeService Recipes
@implements IDisposable

protected override void OnInitialized()
{
    // RecipeSyncWorker holder kopien frisk i baggrunden – siden viser bare kopien og lytter efter ændringer.
    Recipes.Changed += OnRecipesChanged;
}

private void OnRecipesChanged() => InvokeAsync(StateHasChanged);   // kan komme fra en anden tråd
public void Dispose() => Recipes.Changed -= OnRecipesChanged;
```

| Læsning (fra kopien, straks) | |
|---|---|
| `Recipes` | Alle opskrifter A–Å (dansk sortering) |
| `Categories` | Kategorier A–Å med `RecipeCount` |
| `Lookups` | `Difficulties`, `Categories`, `Units` (brugte enheder), `Ingredients` (navn + antal opskrifter, mest brugte først) |
| `Find(id)` / `FindCategory(id)` | Én opskrift/kategori eller `null` |
| `Search(RecipeQuery)` | Samme filtre som API'et, ingen sider. Store/små bogstaver ignoreres også for æøå. |
| `HasData`, `IsConfigured`, `Status` | Til `InfoBox`: `Status.Problem` (`RecipeApiError?`), `LastSuccess`, `IsRefreshing` |

| Opdatering | |
|---|---|
| `RecipeSyncWorker` | Baggrundsjob: henter hele bogen ved start og hvert 15. minut (efter 2 min, hvis det fejlede). Siderne skal ikke selv hente. |
| `EnsureFreshAsync()` | Henter kun, hvis kopien er ældre end 15 min. Sjældent nødvendigt pga. baggrundsjobbet. |
| `RefreshAsync()` | "Opdater nu". Ved fejl bevares den gamle kopi, og `Status.Problem` siger hvorfor. |
| `ReloadRecipeAsync(id)` | Hent én opskrift igen (fx når detaljesiden åbner). `null` = slettet – den forsvinder så også fra kopien. |

| Skrivning (direkte til bogen, derefter kopien + `Changed`) | |
|---|---|
| `CreateRecipeAsync(RecipeDraft)` | Ny opskrift. Kategori via `CategoryId` eller navn (`Category` oprettes, hvis den ikke findes). |
| `UpdateRecipeAsync(id, RecipeDraft)` | **Erstatter** alle felter, ingredienser og trin. Start fra `RecipeDraft.From(recipe)`. Billedet bevares. |
| `DeleteRecipeAsync(id)` | Sletter straks og returnerer den slettede opskrift. |
| `RestoreRecipeAsync(recipe)` | "Fortryd" efter sletning: opretter den igen. **Nyt id og intet billede** (bogen sletter billedfilen). |
| `SetRecipeImageAsync(id, stream, filnavn)` / `RemoveRecipeImageAsync(id)` | Billede: jpg/jpeg/png/webp/gif, højst 10 MB (`RecipeImages`). |
| `CreateCategoryAsync` / `UpdateCategoryAsync` / `DeleteCategoryAsync` | Omdøbning slår igennem på opskrifterne. Sletning gør dem ukategoriserede. Navn i brug → `Conflict`. |

Skrivninger kaster `RecipeApiException`. `Message` er kort og på dansk og kan vises direkte. `Error` fortæller
hvad der gik galt: `NotConfigured`, `Offline`, `Unauthorized`, `NotFound`, `Invalid`, `Conflict` eller `Failed`.
Ved `Invalid` har `FieldError("Title")` API'ets besked til feltet.

## Modellen

**`Recipe`**: `Id`, `Title`, `CategoryId?`, `Category` ("" = ingen), `CategoryIcon`, `PrepTimeMinutes`,
`CookTimeMinutes`, `TotalTimeMinutes`, `Servings`, `Difficulty?`, `Author`, `Notes`, `LastModified` (UTC),
`ImageUrl?`, `Ingredients`, `Steps` + hjælpere `HasCategory`, `HasImage`, `CanScale`.

**`RecipeIngredient`**: `Amount` (fri tekst som skrevet), `Quantity?` (tal, hvis det kan tolkes), `Unit`, `Name`,
`IsHeading`.

**`RecipeDifficulty`**: `Easy`/`Medium`/`Hard` ↔ "Let"/"Middel"/"Svær" (`Label()` giver det danske ord).

**`RecipeQuery`**: `Search`, `Category`, `CategoryId`, `Difficulty`, `MaxTotalMinutes`, `Ingredients` (alle skal
matche en del af et ingrediensnavn), `ModifiedSince`, `Sort` (`Title` A–Å, `LastModified` nyeste først,
`TotalTime` hurtigste først), `Page`/`PageSize` (kun API'et).

## Data, som de ser ud i virkeligheden (vigtigt for brugerfladen)

Målt på den rigtige bog 2026-09-29:

- **Tider mangler ofte.** De fleste opskrifter har `TotalTimeMinutes = 0`. Vis ingen tid frem for "0 min", og vær
  varsom med filteret "under 30 min" (0 betyder "ukendt", ikke "hurtig").
- **Servings er næsten altid udfyldt** (4, 16, 24 …). Ved `Servings = 0` kan opskriften ikke skaleres
  (`CanScale`), og `RecipeMath.Scale` viser så mængderne uændret.
- **Underoverskrifter i ingredienslisten.** Linjer som "Glasur:" (uden mængde og enhed, slutter med kolon) er
  overskrifter: `IsHeading`. Vis dem som en lille overskrift, og lad dem være ude af indkøbslisten.
- **Mængder er fri tekst.** "2 ", "1,5", "2-3", "". `Quantity` er `null`, når teksten ikke er et tal. Vis så
  `Amount` som skrevet.
- **Navne er ikke ensrettede:** "Kærnemælk." / "kærnemælk", "Mel" / "mel", "spiseskefuld" / "spsk".
  `RecipeMath.NormalizeName`/`NormalizeUnit` ensretter dem på indkøbslisten.
- **Trin kan indeholde linjeskift** (`\n`) – vis dem med `white-space: pre-line`.
- **Kategori-ikoner** er Material Symbols-navne (i dag er alle `"cookie"`). Family Hub bruger egne ikoner
  (`IconName`), så lav en lille oversættelse i UI'et, fx på kategoriens navn: Aftensmad → `Utensils`. Brug ikke
  Material-ikonet direkte.
- **Billeder:** kun få opskrifter har et billede. `ImageUrl` er en offentlig, absolut adresse (kræver ingen nøgle)
  og kan bruges direkte i `<img>`. Lav et pænt layout uden billede som standard.
- **Kategorier i dag:** Aftensmad, Brød/pizza, Dessert, Julemad, Kager, Lækkerier/slik.
- **Forfatter:** udelades den ved oprettelse, sætter bogen "Mor".

## API'et (reference)

Rod: `https://opskriftsbog.ramskov.pro/api/v1`. Alle kald kræver `X-Api-Key`. JSON er camelCase.

| Metode og sti | Svar | Service-/klientmetode |
|---|---|---|
| `GET /recipes?search&category&categoryId&difficulty&maxTotalMinutes&ingredient…&modifiedSince&sort&page&pageSize` | 200 `{items, totalCount, page, pageSize, totalPages}` | `SearchRecipesAsync`, `GetAllRecipesAsync` |
| `GET /recipes/{id}` | 200 opskrift / 404 | `GetRecipeAsync` (404 → `null`) |
| `POST /recipes` | 201 opskrift / 400 | `CreateRecipeAsync` |
| `PUT /recipes/{id}` | 200/204 / 400 / 404 | `UpdateRecipeAsync` (henter opskriften, hvis svaret er tomt) |
| `DELETE /recipes/{id}` | 204 / 404 | `DeleteRecipeAsync` |
| `PUT /recipes/{id}/image` (multipart, felt `file`) | 2xx / 400 / 404 | `SetRecipeImageAsync` (henter derefter den nye `imageUrl`) |
| `DELETE /recipes/{id}/image` | 204 / 404 | `RemoveRecipeImageAsync` |
| `GET /categories` | 200 `[ {id, name, icon, recipeCount} ]` | `GetCategoriesAsync` |
| `GET /categories/{id}` | 200 / 404 | `GetCategoryAsync` |
| `POST /categories` | 201 / 400 / 409 | `CreateCategoryAsync` |
| `PUT /categories/{id}` | 2xx / 400 / 404 / 409 | `UpdateCategoryAsync` |
| `DELETE /categories/{id}` | 204 / 404 | `DeleteCategoryAsync` |
| `GET /lookups` | 200 `{difficulties, categories, units, ingredients:[{name, recipeCount}]}` | `GetLookupsAsync` |

Opførsel, der ikke står i OpenAPI-filen, men er målt:

- `lastModified` sendes **uden tidszone**, men er UTC. `modifiedSince` sammenlignes også i UTC (klienten sender `…Z`).
- `pageSize` over 200 skæres ned til 200 (ingen fejl).
- Fejl er ProblemDetails. 401 har dansk `detail` ("Send den delte nøgle i headeren X-Api-Key."). 400 fra
  modelbinding har `errors: { felt: [beskeder] }` på engelsk – derfor tjekker `RecipeValidation` felterne på dansk,
  før der sendes.
- Tekstsøgning i API'et skelner kun store og små bogstaver for a–z (ikke æøå). Den lokale `Search` gør det rigtigt.
- Grænser: titel 200, kategori 100, ikon 50, forfatter 100, mængde/enhed 50, ingrediensnavn 200 tegn;
  tider 0–100.000 min; personer 0–1000.

## Muligheder til madplanens brugerflade

Det kan servicen allerede i dag. UI'et skal kun bygge oven på det:

| Idé | Byggeklodser |
|---|---|
| Vælg ugens retter fra en liste med søgning og kategori-filter | `Search(new RecipeQuery { Search = …, CategoryId = … })`, `Categories` som `HubChoice` |
| "Hvad kan vi lave med …?" (det, der er i køleskabet) | `RecipeQuery.Ingredients`; forslag fra `Lookups.Ingredients` (mest brugte først) |
| Hurtige retter på hverdage | `Sort = RecipeSort.TotalTime`, `MaxTotalMinutes` – husk at 0 = ukendt |
| Opskriftsvisning i køkkenet | `Ingredients` (med `IsHeading`), `Steps` som nummererede trin med stor tekst, `Notes` i en `InfoBox` |
| Antal personer | `NumberStepper` → `RecipeMath.Scale(recipe, personer)` – mængder som "1½ dl" |
| Indkøbsliste fra ugens retter | **Bygget** (2026-10-02): `Shopping/ShoppingCalculator` – omregner enheder, runder op til hele pakker og holder basisvarer udenfor. Se modulets README. (`RecipeMath.CombineForShopping` er den simple udgave: samme vare og enhed lægges sammen.) |
| Nye/ændrede opskrifter | `Sort = RecipeSort.LastModified` |
| Rette en opskrift fra køkkenet | `RecipeDraft.From(recipe)` → felter → `RecipeValidation.Validate` → `UpdateRecipeAsync` |
| Slet med fortryd | `DeleteRecipeAsync` + `Toasts.Undoable` → `RestoreRecipeAsync` (nyt id, billedet er væk) |
| Status | `InfoBox` ved `!IsConfigured` eller `Status.Problem is not null` ("Viser opskrifterne fra i går – opskriftsbogen kan ikke nås") – aldrig toasts |

Husk ved madplanens egne data: gem kun `Recipe.Id` (+ antal personer) i madplanen, og slå opskriften op med
`Find(id)`. Vis en venlig tekst, hvis den er slettet i bogen (`null`). Madplanen er familiens egne data (EF Core +
SQLite i modulet). Opskrifterne ejes af opskriftsbogen.
