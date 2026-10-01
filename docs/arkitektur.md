# Arkitektur

## Overblik

```
Homelab-server (Docker, x64 eller arm64)
└─ container "familyhub" ──► FamilyHub.Web (ASP.NET Core / Blazor Server, .NET 10)
     restart: unless-stopped    port 8080 (fast)    volumen: /data
     indstillinger + Google-nøgler fra .env
                    ▲
                    │ http + WebSocket (SignalR) over hjemmenettet
                    │
Raspberry Pi 5 (Raspberry Pi OS, 64-bit)
└─ skrivebord (labwc) ──► Chromium i kiosktilstand ──► http://homelab.local:8080
                           (19" touchskærm, dansk skærmtastatur i appen)
```

Al logik er C#. Chromium står kun for visning og touch. Blazor Server holder én forbindelse (SignalR)
pr. skærm. På et kablet hjemmenet er ventetiden umærkelig; Pi'en bør derfor sidde på kabel.

Konsekvenser af at server og skærm er adskilt:

- Er serveren nede, er skærmen det også. Kiosken venter på `/health`, og appen genforbinder selv.
- Google-login kræver https eller `localhost` – enten via jeres https-domæne (reverse proxy) eller fra den bærbare
  gennem en SSH-tunnel (`deploy/forbind-google.ps1`).
- Appen har ingen login endnu. Adgangen til porten begrænses i netværket (VLAN, reverse proxy), ikke i appen.

## Hvorfor Blazor Server + Chromium-kiosk?

| Krav | Løsning |
|---|---|
| C# og .NET 10 | Blazor – komponenter, services og logik i C#. |
| Pålidelig touch, skrift og grafik på Pi'en | Chromium: moden touch-håndtering og GPU-rendering på Raspberry Pi. |
| Roligt, fleksibelt design | HTML/CSS med design-tokens – nemt at holde ensartet og få nattilstand. |
| Robusthed | Docker genstarter containeren; kiosken venter på `/health` og genindlæser selv efter opdateringer. |
| Brug fra flere skærme | Samme server kan åbnes fra flere skærme og telefoner på hjemmenettet. |

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
| `CalendarService` (Kalender) | Singleton | Google-konti, kalendere, lokal kopi af aftaler; `CalendarSyncWorker` synkroniserer i baggrunden |
| `RecipeService` (Madplan) | Singleton | Kopi af familiens opskriftsbog (API) i hukommelsen og på disk; søgning og skrivninger. `RecipeSyncWorker` opdaterer den hvert 15. minut. Se `docs/opskrifter-api.md` |
| `MealPlanService` (Madplan) | Singleton | Ugens aftensmad i SQLite (`madplan/madplan.db`, EF Core med migrationer); `Changed` til alle skærme |
| `PrintService` (Core) | Singleton | Print en PDF via familiens printserver (Print API) fra alle menuer; `PrintJobWorker` følger udskrifterne og sender `JobChanged` til alle skærme. Se `docs/printer.md` |
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
- **Proces:** Docker `restart: unless-stopped` + healthcheck på `/health`; kiosk-scriptet genstarter Chromium, hvis den lukkes.
- **Inaktivitet:** skærmen kan vende tilbage til forsiden efter X minutter (indstilles pr. skærm).

## Beslutninger

| Dato | Beslutning |
|---|---|
| 2026-09-25 | Blazor Server (.NET 10) + Chromium-kiosk på Raspberry Pi 5. |
| 2026-09-25 | Ingen prerendering – komponenters livscyklus kører præcis én gang. |
| 2026-09-25 | Eget skærmtastatur i appen (dansk, touch-bevidst) frem for styresystemets. |
| 2026-09-25 | Én menu = ét modul-projekt med en `HubModule`-klasse. |
| 2026-09-25 | Data: JSON-filer til indstillinger; EF Core + SQLite pr. modul, når der kommer rigtige data. |
| 2026-09-25 | Appen lytter kun på localhost, indtil der er login. *Erstattet 2026-09-28.* |
| 2026-09-26 | Kalender: Google via OAuth for installerede apps (kode + PKCE, loopback-redirect). Login på skærmen eller fra den bærbare via SSH-tunnel – så appen kan blive på localhost. Se `docs/google-kalender.md`. |
| 2026-09-26 | Kalender: ingen Google-SDK – få HTTP-kald. Lokal kopi af aftalerne på disk, så skærmen virker uden net. |
| 2026-09-28 | Serveren kører i Docker i homelab'en; Raspberry Pi'en er kun kiosk. Indstillinger og Google-nøgler i `.env` (skabelon `.env.example`), data på en volumen (`/data`). Appen lytter på hjemmenettet; adgangen begrænses i netværket, indtil der er login. Google-login via SSH-tunnel til serveren. |
| 2026-09-28 | Google-login er også tilladt fra en https-adresse (reverse proxy med eget domæne) med en »Webapplikation«-klient. Tunnelen er stadig muligheden uden https. |
| 2026-09-29 | Madplan: opskrifterne ejes af familiens opskriftsbog (separat app med API, nøgle i `.env`). Family Hub holder en kopi af hele bogen (lille) på disk, søger lokalt og skriver direkte til API'et. Madplanen gemmer kun opskrifternes id. Se `docs/opskrifter-api.md`. |
| 2026-09-29 | Madplan: kun aftensmad, én ret pr. dag, mandag–søndag. Første modul med EF Core + SQLite; databasen oprettes/opgraderes med migrationer (`MigrateAsync`) ved første brug. Flyt til en dag med en ret = byt plads. Swipe og træk-og-slip er genveje – alt kan også gøres med tryk. |
| 2026-10-01 | Print: via familiens printserver (Print API, separat app) – Family Hub sender PDF'er og følger jobbet. Ligger i Core, så alle menuer kan printe uden at referere til hinanden. Family Hub får en almindelig nøgle (må kun printe), aldrig printserverens master-nøgle, da Family Hub ingen login har. Se `docs/printer.md`. |
