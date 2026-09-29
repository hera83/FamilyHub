# Modul: Madplan

**Status:** opskrifterne er klar (service mod familiens opskriftsbog); brugerfladen er ikke bygget – kun en "på vej"-side.

## Opskrifter

Retterne kommer fra familiens opskriftsbog via dens API. Læs **[docs/opskrifter-api.md](../../../docs/opskrifter-api.md)**
før brugerfladen bygges – den beskriver API'et, data som de faktisk ser ud, og hvad `RecipeService` kan.

| Fil (`Recipes/`) | Indhold |
|---|---|
| `RecipeService.cs` | Singleton til brugerfladen: lokal kopi (virker offline), søgning, skrivninger, `Changed`, `Status` |
| `RecipeApiClient.cs` | Ét kald pr. endpoint + oversættelse af API'ets JSON (`RecipeMapping`) |
| `RecipeModels.cs` | `Recipe`, `RecipeIngredient`, `RecipeCategory`, `RecipeQuery`, `RecipeDraft` … |
| `RecipeMath.cs` | Skalering til antal personer, danske mængder (1½), samlet indkøbsliste |
| `RecipeSearch.cs` / `RecipeValidation.cs` | Lokal søgning; tjek af felter før afsendelse |
| `RecipeApiOptions.cs` / `RecipeApiException.cs` | Opsætning (adresse + nøgle) og fejl med danske beskeder |

## Tanker til indhold (ikke besluttet)

- Ugeplan (mandag–søndag) med én ret pr. dag; retter vælges fra en liste over familiens retter.
- Antal personer pr. ret med `NumberStepper` – ikke tastatur.
- Indkøbsliste, der samles automatisk ud fra ugens retter, plus manuelle varer
  ("Tilføj flere i træk"-mønsteret med `KeepKeyboardOnEnter`).
- Afkrydsning i butikken fra telefonen (Family Hub kan åbnes på hjemmenettet).
- Widget på forsiden: "I aften" og antal varer på listen.
- Data i SQLite via EF Core (én `DbContext` for modulet) i `IAppDataPaths.GetDirectory("madplan")`.

Byg modulet efter [docs/standarder/nyt-modul.md](../../../docs/standarder/nyt-modul.md).
