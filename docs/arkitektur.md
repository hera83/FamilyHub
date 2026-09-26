# Arkitektur

## Overblik

```
Raspberry Pi 5 (Raspberry Pi OS, 64-bit)
├─ systemd: familyhub.service ──► FamilyHub.Web (ASP.NET Core / Blazor Server, .NET 10)
│                                  http://localhost:5000   data: /var/lib/familyhub
└─ skrivebord (labwc) ──► Chromium i kiosktilstand ──► http://localhost:5000
                           (19" touchskærm, dansk skærmtastatur i appen)
```

Al logik er C#. Chromium står kun for visning og touch. Blazor Server holder én forbindelse (SignalR)
pr. skærm; på localhost er det hurtigt og uden ventetid.

## Hvorfor Blazor Server + Chromium-kiosk?

| Krav | Løsning |
|---|---|
| C# og .NET 10 | Blazor – komponenter, services og logik i C#. |
| Pålidelig touch, skrift og grafik på Pi'en | Chromium: moden touch-håndtering og GPU-rendering på Raspberry Pi. |
| Roligt, fleksibelt design | HTML/CSS med design-tokens – nemt at holde ensartet og få nattilstand. |
| Robusthed | systemd genstarter appen; kiosken venter på `/health` og genindlæser selv efter opdateringer. |
| Senere: brug fra telefonen | Samme app kan åbnes fra telefoner på hjemmenettet (kræver én linje i servicen). |

Alternativer, der blev fravalgt: **Avalonia** (native, men sværere touch-/tastaturhåndtering og design på Pi'en),
**.NET MAUI** (understøtter ikke Linux).

## Projekter

```
FamilyHub.Core      ren logik, ingen UI: konfiguration, tid, toasts, husstand, lagring, tastaturpolitik
   ▲
FamilyHub.UI        designsystemet: komponenter, skærmtastatur, services pr. skærm, modul-kontrakten, CSS/JS
   ▲        ▲
   │   FamilyHub.Modules.*   én menu pr. projekt (Kalender, Madplan, …) – kender kun Core og UI
   │        ▲
FamilyHub.Web       værten: Program.cs, layout, navigation, forside, indstillinger, registrering af moduler
```

- Moduler må ikke referere til hinanden eller til `FamilyHub.Web`.
- `tests/FamilyHub.Tests` tester Core, UI (bl.a. med bUnit) og opstart af Web.

## Indstillinger: pr. skærm eller for hele familien?

| | Denne skærm (`DeviceService`) | Hele familien (`IHouseholdService`) |
|---|---|---|
| Eksempler | Skærmtastatur, tema, visningsstørrelse, tilbage til forsiden | Husstandens navn, familiemedlemmer |
| Gemmes | Browserens localStorage på den skærm | `household.json` i datamappen |
| Levetid | Scoped (én pr. forbindelse) | Singleton – ændringer sendes til alle skærme |

## Services og levetid

| Service | Levetid | Rolle |
|---|---|---|
| `IHubClock` | Singleton | Tid i Europe/Copenhagen, ét fælles minut-tik |
| `IHouseholdService` | Singleton | Familiens indstillinger, `Changed`-event til alle skærme |
| `IAppDataPaths` | Singleton | Datamappen og undermapper pr. modul |
| `ModuleCatalog` | Singleton | Registrerede moduler, navigation og widgets |
| `IToastService` | Scoped | Toasts på denne skærm |
| `DeviceService` | Scoped | Skærmens egenskaber og egne indstillinger |
| `KeyboardService` | Scoped | Skærmtastaturets tilstand |
| `DialogService` | Scoped | Bekræftelsesdialoger |

## Robusthed

- **Opstart:** konfiguration valideres (`ValidateOnStart`) – fx en ukendt tidszone stopper opstarten med en klar besked.
- **Data:** `JsonFileStore` skriver atomisk (temp-fil + omdøb), så strømsvigt aldrig efterlader en halv fil.
  En ødelagt fil flyttes til side (`*.corrupt-…`), og appen starter alligevel.
- **Fejl i en side:** `ErrorBoundary` i layoutet viser en venlig besked; navigation og resten virker.
- **Knapper:** deaktiveres mens de arbejder og laver fejl om til toasts.
- **Forbindelse:** genforbinder selv; genindlæser først, når `/health` svarer (så Chromium aldrig ender på en fejlside).
- **Proces:** systemd `Restart=always`; kiosk-scriptet genstarter Chromium, hvis den lukkes.
- **Inaktivitet:** skærmen kan vende tilbage til forsiden efter X minutter (indstilles pr. skærm).

## Beslutninger

| Dato | Beslutning |
|---|---|
| 2026-09-25 | Blazor Server (.NET 10) + Chromium-kiosk på Raspberry Pi 5. |
| 2026-09-25 | Ingen prerendering – komponenters livscyklus kører præcis én gang. |
| 2026-09-25 | Eget skærmtastatur i appen (dansk, touch-bevidst) frem for styresystemets. |
| 2026-09-25 | Én menu = ét modul-projekt med en `HubModule`-klasse. |
| 2026-09-25 | Data: JSON-filer til indstillinger; EF Core + SQLite pr. modul, når der kommer rigtige data. |
| 2026-09-25 | Appen lytter kun på localhost, indtil der er login. |
