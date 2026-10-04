# Modul: Dexcom

Blodsukker fra Dexcom på køkkenskærmen: målingen **lige nu** (stor, med trendpil og hvor gammel den er),
en **graf** over de sidste 1, 3, 6, 12 eller 24 timer og **tid i målområdet** for 7, 14, 30 eller 90 dage.
Data kommer fra familiens eget API foran Dexcom via `IDexcomService` i Core – se [docs/dexcom.md](../../../docs/dexcom.md).

Ingen alarmer og ingen lyd (skærmen har ingen højttaler). Lavt blodsukker farves rødt og højt gult –
altid sammen med ikon og ord ("Lav", "Høj", "I målområdet"), aldrig farve alene.

## Sider og ruter

| Rute | Indhold |
|---|---|
| `/dexcom` | Lige nu, graf og tid i målområdet. Tandhjulet øverst til højre fører til indstillingerne. |
| `/dexcom/indstillinger` | Målområde (lav/høj med steppere), grafens periode ved start, forbindelsens status. |

Forsiden får kortet **Blodsukker** (`GlucoseWidget`): tallet, trendpilen og om det er lavt, i målområdet eller højt.
Det vises kun, mens målingen er aktuel (8 minutter) – ellers fylder det ingenting, ligesom »I aften«.

## Regler

| Regel | Hvor |
|---|---|
| En måling er "lige nu" i **8 minutter**. Derefter vises "–" og "Ingen ny måling i 12 min". | `GlucoseRules.StaleAfter` |
| Grænserne hører til målområdet: 3,9 og 10,0 er "I målområdet". | `GlucoseRules.Classify` |
| Standard 3,9–10,0 mmol/L. Lav kan sættes 3,0–6,0, høj 7,0–20,0 (i trin af 0,1). | `GlucoseSettings` |
| Tid i målområdet = andel af målingerne (de kommer hvert 5. min). Procenterne giver altid 100. | `TimeInRange` |
| Dækker målingerne ikke hele perioden, står der hvor mange dage tallet bygger på. | `TimeInRange.CoveredDays` |
| Grafens top er 14, 18 eller 22 mmol/L – den mindste, der er plads til. Bunden er 2. | `GlucoseChartLayout` |
| Hele siden står på skærmen uden at rulle (fra ca. 720 px højde): grafen får den højde, der er tilbage. Rettes noget i siden, skal `--dx-reserved` passe til. | `DexcomPage.razor.css` |

## Opbygning

| Mappe | Indhold |
|---|---|
| `Glucose/` | `GlucoseMonitor` (singleton: 90 dages målinger i hukommelsen, indstillinger, `Changed`), `GlucoseSyncWorker` (hvert minut), `GlucoseRules`, `TimeInRange`, `GlucoseHistory` (flet og udsnit), `GlucoseSettings` – ren logik med tests |
| `Components/` | `GlucoseWidget` (forsiden), `CurrentGlucose` (lige nu), `GlucoseChart` + `GlucoseChartLayout` (SVG uden JavaScript), `TimeInRangeCard`, `TrendArrow` |
| `Pages/` | `DexcomPage`, `DexcomSettingsPage` |

## Hentning

Alle skærme deler én kopi, så der kommer ét kald i minuttet, uanset hvor mange skærme der er åbne:

1. Ved opstart: det sidste døgn først (så siden fyldes med det samme), derefter op til 90 dage tilbage i bidder af 30 dage.
2. Hvert minut: de sidste 3 timer. Hver halve time: hele det sidste døgn – så målinger, Dexcom leverer for sent
   efter et tabt signal, også kommer med.
3. Fejler det, bliver de gamle målinger stående. Siden viser en `InfoBox`, når det har varet over 3 minutter.

Målingerne gemmes ikke på disk – API'et har dem. Efter en genstart hentes de igen.

## Data (i `dexcom/` i datamappen)

| Fil | Indhold |
|---|---|
| `indstillinger.json` | Målområde og grafens periode ved start – fælles for alle skærme |
