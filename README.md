# Family Hub

Familiens køkkenskærm – kalender, madplan og mere – på en Raspberry Pi 5 med 19" touchskærm.
Bygget i C# med .NET 10 (Blazor Server) og vist i Chromium i kiosktilstand.

**Status:** Fundamentet er på plads: app-skal med navigation, designsystem, dansk skærmtastatur med
touch-detektion, toasts og informationsbokse, indstillinger pr. skærm og for familien, nattilstand og
opsætning til Raspberry Pi. Menuerne *Kalender* og *Madplan* er skeletter, klar til at blive bygget.

## Kom i gang

Kræver .NET 10 SDK.

```powershell
dotnet run --project src/FamilyHub.Web     # åbn http://localhost:5080
dotnet test                                # kør alle tests
```

Gå til **Indstillinger → Komponentoversigt** for at se og afprøve alle byggeklodserne –
også skærmtastaturet, hvis din skærm har touch.

## Struktur

```
src/
  FamilyHub.Core/        Logik uden UI: tid, toasts, husstand, lagring, tastaturpolitik
  FamilyHub.UI/          Designsystemet: komponenter, skærmtastatur, CSS-tokens, modul-kontrakt
  FamilyHub.Web/         App-skallen: layout, navigation, forside, indstillinger
  Modules/
    FamilyHub.Modules.Calendar/   Kalender (skelet)
    FamilyHub.Modules.MealPlan/   Madplan og indkøbsliste (skelet)
tests/FamilyHub.Tests/   Unit-, komponent- og opstartstests
tools/ui-smoke/          Browser-test af touch og skærmtastatur (Playwright + Chrome)
deploy/                  Byg og installér på Raspberry Pi
docs/                    Arkitektur, standarder og Pi-vejledning
```

## Dokumentation

- [Arkitektur](docs/arkitektur.md) – hvordan det hænger sammen, og hvorfor
- [Designstandarder](docs/standarder/README.md) – layout, komponenter, feedback, input og tastatur, tekst, nye menuer
- [Raspberry Pi](docs/raspberry-pi.md) – installation og opdatering

## Raspberry Pi

```powershell
.\deploy\publish-pi.ps1 -PiHost familyhub.local -PiUser pi -Install   # første gang
.\deploy\publish-pi.ps1 -PiHost familyhub.local                       # opdateringer
```

Se [docs/raspberry-pi.md](docs/raspberry-pi.md) for hele vejledningen.
