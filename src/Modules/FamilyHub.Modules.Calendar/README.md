# Modul: Kalender

Familiens Google-kalendere på køkkenskærmen: **dag** (en kolonne pr. familiemedlem), **uge** (standard) og
**måned** (med ugenumre). Aftaler kan tilføjes – kun titlen skrives; kalender, dag og tid vælges.
Tryk på en aftale: **Ret** (titel, dag og tid – sted, beskrivelse og gæster bliver, som de er i Google) og
**Slet** (sker straks, med »Fortryd«). En gentaget aftale ændres/slettes kun denne gang. Invitationer fra andre
og Googles egne aftaler (fødselsdage, aftaler fra Gmail) kan ikke ændres herfra.
Opsætning af Google: [docs/google-kalender.md](../../../docs/google-kalender.md).

## Sider og ruter

| Rute | Indhold |
|---|---|
| `/kalender` | Kalenderen. `?visning=dag\|maaned` og `?dato=yyyy-MM-dd` (uden = denne uge / i dag). |
| `/kalender/indstillinger` | Google-konti, valg af kalendere (vis/skjul, familiemedlem, farve), opdatering. |
| `/kalender/google/callback` | Google sender browseren hertil efter login. |

Forsiden får widgetten **I dag** (resten af dagens aftaler, ellers den næste).

## Opbygning

| Mappe | Indhold |
|---|---|
| `Google/` | `GoogleOAuthClient` (login med PKCE, fornyelse), `GoogleCalendarApi` (kalendere, aftaler, opret, ret, slet), `GoogleCredentialsProvider` (nøglefil / user-secrets) |
| `Services/` | `CalendarService` (singleton: konti, kalendere, lokal kopi, `Changed`), `CalendarSyncWorker` (hvert 5. min), `CalendarLayout` (datoer, placering i tidsgitter – ren logik), `CalendarMatching` (gæt familiemedlem og farve) |
| `Components/` | `WeekView`, `DayView`, `MonthView`, `EventCard`, `EventDetailsDialog` (Ret/Slet), `EventFormDialog` (Tilføj og Ret), `TodayWidget` |
| `Pages/` | `CalendarPage`, `CalendarSettingsPage`, `GoogleCallbackPage` |

## Data (i `kalender/` i datamappen)

| Fil | Indhold |
|---|---|
| `google-client.json` | Nøglefilen fra Google Cloud (lægges der af jer) |
| `google-konti.json` | Forbundne konti med krypteret refresh token |
| `kalendere.json` | Valg pr. kalender: vist, familiemedlem, farve |
| `cache.json` | Lokal kopi af kalendere og aftaler (2 mdr. tilbage – 13 mdr. frem) |

## Afprøv uden Google

```powershell
cd tools/ui-smoke
node demo-data.mjs                                     # familie, kalendere og aftaler omkring i dag
$env:FamilyHub__DataDirectory = (Resolve-Path ./demo-data)
dotnet run --project ../../src/FamilyHub.Web
```

## Idéer til senere

- Flytte en aftale til en anden kalender, og rette hele serien af en gentaget aftale.
- Gentagne aftaler ("hver tirsdag") i Tilføj-dialogen.
- Egne navne til kalendere (i dag bruges navnet fra Google).
