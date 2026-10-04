# Blodsukker (Dexcom)

Family Hub læser blodsukker fra familiens eget API foran Dexcom (`https://dexcom.appcore.cc`).
Koden ligger i `src/FamilyHub.Core/Dexcom/` og bruges via `IDexcomService` – så forsiden, pauseskærmen og en menu kan dele den.
Menuen **Dexcom** (`src/Modules/FamilyHub.Modules.Dexcom/`) viser målingerne – se modulets README.

## Opsætning

| Miljø | Adresse | Nøgle |
|---|---|---|
| Udvikling | `appsettings.json` (`FamilyHub:Dexcom:BaseUrl`) | `dotnet user-secrets set "FamilyHub:Dexcom:ApiKey" "ak_…" --project src/FamilyHub.Web` |
| Server (Docker) | `DEXCOM_API_URL` i `.env` | `DEXCOM_API_KEY` i `.env` |

Brug en almindelig nøgle (`ak_…`) oprettet til Family Hub med `POST /Keys/Create` – aldrig API'ets master key.
Mangler nøglen, er `IsConfigured` falsk, og alle kald giver `DexcomError.NotConfigured`.

## Kald

| Metode | API | Svar |
|---|---|---|
| `GetLatestAsync()` | `GET /Dexcom/Latest` | `GlucoseReading` (tid, mmol/L, trend). Tjek tiden – målingen kan være gammel. |
| `GetReadingsAsync(from, to)` | `GET /Dexcom/Readings` | `GlucoseReadings`, ældste først. Perioden sendes i UTC. |
| `GetStatusAsync()` | `GET /Health` | `DexcomStatus`. 503 er et normalt svar (målingerne er gået i stå). |

`GlucoseTrend` har `Label()` (fx "Falder hurtigt") og `Arrow()` (↑↑ ↑ ↗ → ↘ ↓ ↓↓).
Fejl kommer som `DexcomException` med en kort dansk besked, klar til en toast eller `InfoBox`.
`/Keys` og `/Log` kræver master key og bruges ikke af Family Hub.
