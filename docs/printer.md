# Printer: Print API og PrintService

Family Hub printer gennem familiens **printserver** ("Print API"). Det er en separat app i homelab'en
(projektet `PrintAPI`), der tager imod PDF'er, sætter dem i kø og sender dem til HP-printeren over IPP.
Family Hub sender kun PDF'en og følger jobbet. Den kender ingen printerdrivere.

Koden ligger i `src/FamilyHub.Core/Printing/` (i Core, fordi alle menuer skal kunne printe).
Tests: `tests/FamilyHub.Tests/Core/PrintServiceTests.cs`.

## Opsætning

| Hvad | Hvor |
|---|---|
| Adresse | `FamilyHub:Printing:BaseUrl`, fx `http://192.168.1.10:8080`. Tom = ikke sat op |
| Nøgle (hemmelig) | `FamilyHub:Printing:ApiKey`, sendes som headeren `x-api-key` |
| Printer | `FamilyHub:Printing:PrinterId`. Tom = den eneste printer, printserveren kender |
| Server (Docker) | `PRINT_API_URL=`, `PRINT_API_KEY=` og evt. `PRINT_PRINTER_ID=` i `.env`, derefter `docker compose up -d` |
| Udvikling | `dotnet user-secrets set "FamilyHub:Printing:ApiKey" "<nøgle>" --project src/FamilyHub.Web` |

Mangler adresse eller nøgle, starter appen alligevel. `PrintService.IsConfigured` er så `false`, og
skærmen skal vise en `InfoBox` i stedet for en printknap.

### Nøglen: en almindelig nøgle, aldrig master-nøglen

Printserveren har én master-nøgle (administration) og almindelige nøgler (`ak_…`), der kun må printe.
Family Hub har ingen login, så den får en **almindelig nøgle**. Opret den én gang med master-nøglen
(fra den bærbare eller i Swagger UI på `http://<printserver>:8080`):

```powershell
$master = "<MASTER_KEY fra printserverens .env>"
Invoke-RestMethod -Method Post -Uri http://<printserver>:8080/Keys/Create `
  -Headers @{ "x-api-key" = $master } -ContentType application/json `
  -Body '{ "name": "Family Hub" }'
```

`key` i svaret vises **kun én gang**. Læg den i `.env` som `PRINT_API_KEY`. En mistet nøgle kan rulles over
med `/Keys/Rollover/{id}/rollover`.

Printeren registreres også med master-nøglen: `POST /Hp/Register` med `{ "host": "<printerens IP>" }`.
Svaret har printerens `id`. Se printserverens README.

### Netværk

- Printserveren kører med `network_mode: host` og lytter altid på værtens **port 8080**. Family Hub bruger
  også 8080. Kører de på **samme** server, kan de ikke starte begge to – så skal den ene flyttes.
- Fra Family Hubs container er `localhost` containeren selv. Brug serverens IP-adresse (eller et navn, Docker
  kan slå op) i `PRINT_API_URL`.
- Printserveren taler kun http. Den skal blive på hjemmenettet.

## Tre lag – hvad bruges hvornår?

| Klasse | Levetid | Brug den til |
|---|---|---|
| `PrintService` | Singleton | **Alt i brugerfladen.** Print en PDF, printerstatus, annuller, `JobChanged`-event og `ActiveJobs`. |
| `PrintApiClient` | Singleton | Rå adgang, ét kald pr. endpoint (`/Health` og `/Print/*`). Sjældent nødvendigt. |
| `PrintTexts` / `PrintValidation` | Statiske | Danske tekster for printerens beskeder; tjek af PDF, kopier og sider før afsendelse. Rene funktioner. |

`PrintJobWorker` (baggrundsjob) følger de udskrifter, Family Hub har sendt, hvert 3. sekund, til de er færdige.
Den sover, når intet printes, og så kaldes printserveren slet ikke.

## PrintService – kort vejledning

```csharp
@inject PrintService Printing
@inject IToastService Toasts
@implements IDisposable

@if (!Printing.IsConfigured)
{
    <InfoBox>Printeren er ikke sat op endnu.</InfoBox>
}
else
{
    <HubButton Icon="IconName.Printer" OnClick="PrintAsync">Print</HubButton>
}

@code {
    protected override void OnInitialized() => Printing.JobChanged += OnJobChanged;

    private async Task PrintAsync()
    {
        byte[] pdf = ...;                                   // modulet laver selv PDF'en
        await Printing.PrintPdfAsync(pdf);                  // HubButton viser fejl som toast
        Toasts.Success("Sendt til printeren");
    }

    private void OnJobChanged(PrintJob job) => InvokeAsync(StateHasChanged);   // kan komme fra en anden tråd
    public void Dispose() => Printing.JobChanged -= OnJobChanged;
}
```

## Printknapper i dag

| Hvor | Hvad | Kode |
|---|---|---|
| Madplan → tryk på en planlagt ret | **Print** nederst til venstre (kun ved opskrifter fra bogen, og kun når printeren er sat op) | `DinnerDialog.razor`, `Print/RecipePrinter.cs`, `Print/RecipePdf.cs` |

**Opskriften som PDF** (`RecipePdf`, QuestPDF): A4 med kategori, titel, tid/personer/sværhed, billedet, ingredienser
i en boks til venstre (underoverskrifter som "GLASUR"), nummererede trin til højre og noter nederst. Lange
opskrifter fortsætter på næste side ("Side 1 af 2"). Farverne er til papir (ikke design-tokens) og kan læses i
sort/hvid.

`RecipePrinter.PrintAsync(recipe)`: henter billedet (højst 10 sek. – ellers printes uden), laver PDF'en uden for
skærmens tråd og sender den. **I farver, når der er et billede**, ellers sort/hvid. Dialogen viser
"Sendt til printeren", og senere en toast, hvis printeren går i stå ("Printeren mangler papir.") eller
udskriften mislykkes.

QuestPDF bruger Community-licensen (gratis for privatpersoner og små virksomheder). Skrifttypen (Lato med æøå og ½)
følger med pakken – Docker-imaget skal ikke have ekstra skrifttyper.

| Metode | |
|---|---|
| `PrintPdfAsync(pdf, options?)` | Tjekker PDF og indstillinger, finder printeren, sender. Svarer straks med jobbet (normalt `Queued`). `byte[]`/`ReadOnlyMemory<byte>` eller `Stream`. |
| `GetStatusAsync()` | Printerne og om de svarer lige nu (`PrintServerStatus` → `Printers`, `IsHealthy`). Tager op til ca. 3 sek. |
| `ActiveJobs` | Ufærdige udskrifter sendt fra Family Hub, ældste først. |
| `JobChanged` | Et job er sendt, er gået videre eller færdigt. Fyrer kun, når noget synligt ændrer sig. |
| `GetJobAsync(id)` | Jobbets status nu. `null` = printserveren kender det ikke (længere). |
| `GetJobsAsync(query?)` | Alle job sendt med Family Hubs nøgle, nyeste først (`PrintJobQuery`: printer, status, side). |
| `CancelAsync(id)` | Fjerner jobbet fra køen eller stopper det på printeren. `Conflict`, mens det sendes, eller når det er færdigt. |

**`PrintOptions`**: `PrinterId` (tom = indstillingen eller den eneste printer), `Copies` 1–99, `Color` (standard
sort/hvid), `Duplex` (begge sider, vendes på den lange led), `Pages` ("2-9", "5", "1,3,5-7", "3-"; tom = alle).

**`PrintJob`**: `Status` + `StatusText` ("I kø", "Sendes til printeren", "Udskriver", "Udskrevet",
"Mislykkedes", "Annulleret"), `IsFinished`, `Problem` (dansk, fx "Printeren mangler papir.", ellers `null`),
`PagesToPrint`, `Copies`, `ImpressionsCompleted`, tider i UTC. `StatusMessage` er printserverens engelske
forklaring – kun til loggen.

**`PrinterInfo`**: `Name`, `IsOnline`, `IsReady`, `Problem` (dansk) + de rå IPP-felter.

### Fejl

Alt kaster `PrintApiException`. `Message` er kort og på dansk og kan vises direkte. `Error`:

| `Error` | Betyder | Besked |
|---|---|---|
| `NotConfigured` | Adresse eller nøgle mangler | Printeren er ikke sat op endnu. |
| `Offline` | Printserveren kan ikke nås | Printserveren kan ikke nås lige nu. |
| `Unauthorized` | Nøglen blev afvist | Printserveren afviste nøglen. |
| `NoPrinter` | Ingen printer valgt, og printserveren har ingen eller flere | Der er flere printere. Vælg, hvilken Family Hub skal bruge. |
| `NotFound` | Printeren eller jobbet findes ikke | Printeren findes ikke på printserveren. / Udskriften findes ikke længere. |
| `Invalid` | Ikke en PDF, forkerte sider/kopier, kan ikke duplex | Fx "Kun PDF-dokumenter kan udskrives." |
| `Conflict` | Printeren er slået fra, eller jobbet er i gang/færdigt | Fx "Udskriften er allerede færdig." |
| `PrinterUnavailable` | Printserveren svarer, men printeren gør ikke | Printeren svarer ikke. |
| `Failed` | Alt andet – detaljer i loggen | Printserveren svarede med en fejl. |

## Feedback på skærmen (efter `docs/standarder/`)

- Sendt: `Toasts.Success("Sendt til printeren")` – selve udskriften er resultatet.
- Problemer undervejs (papir, blæk, printeren svarer ikke): vis `job.Problem` i en `InfoBox`, så længe jobbet er
  i `ActiveJobs`. Ikke en toast pr. ændring.
- Printeren er ikke sat op eller nede: `InfoBox` med `PrinterInfo.Problem` – aldrig en toast ved sidevisning.
- Annuller er en almindelig handling uden "Er du sikker?".

## Printserverens API (reference)

Alle kald kræver `x-api-key` (undtagen `/Health`). JSON er camelCase. Fejl er ProblemDetails med engelsk `detail`.

| Metode og sti | Adgang | Svar | Klientmetode |
|---|---|---|---|
| `GET /Health` | Alle | 200 / **503 med samme indhold**, når ingen printer er online | `GetStatusAsync` |
| `POST /Print/Submit` | Alle nøgler | **202** med jobbet / 400 / 404 printer / 409 slået fra / 422 kan ikke | `SubmitAsync` |
| `GET /Print/GetStatus/{id}` | Alle nøgler | 200 / 404 | `GetJobAsync` (404 → `null`) |
| `GET /Print/GetAll?PrinterId&Status&Page&PageSize` | Alle nøgler | 200 `{items, totalCount, page, pageSize}` | `GetJobsAsync` |
| `POST /Print/Cancel/{id}` | Alle nøgler | 200 / 404 / 409 / 502–504 printeren svarer ikke | `CancelAsync` |
| `/Hp/*`, `/Keys/*`, `GET /Log` | Kun master-nøglen | | Bruges ikke af Family Hub |

Opførsel, der ikke står i OpenAPI-filen:

- Kun PDF (base64, gerne med `data:application/pdf;base64,`-præfiks). Højst ca. 75 MB.
- En almindelig nøgle ser kun sine egne job.
- Jobbet går `Queued` → `Processing` → `Printing` → `Completed`/`Failed`/`Canceled`. En printer, der ikke svarer,
  prøves 5 gange (30 sek. → 5 min.). Et job sendes aldrig to gange.
- `printerJobState = "processing-stopped"` betyder, at printeren venter på noget (papir, låg, blæk).
- Tider er UTC, nogle gange uden `Z`.
- HP-blækprinterne tager ikke PDF direkte. Printserveren laver dem selv om til PWG-raster (300 dpi).
