# Google Kalender

Kalendermenuen viser familiens Google-kalendere (dag, uge og måned) og kan tilføje aftaler.
Opsætningen sker én gang og tager omkring 15 minutter.

## Sådan hænger det sammen

```
Google Cloud (jeres eget lille projekt)  →  nøglefil: client_secret_….json
             │
client_id + client_secret  →  .env på serveren  →  docker compose up -d
             │
forbind-google.ps1 → »Forbind Google-konto«  →  log ind hos Google (én gang pr. konto)
             │
Family Hub gemmer en krypteret adgang og henter kalenderne hvert 5. minut
             │
Serveren har en lokal kopi – køkkenskærmen virker, også når internettet driller
```

- **Hvad Family Hub må:** se listen over jeres kalendere og se/tilføje aftaler
  (`calendar.calendarlist.readonly` + `calendar.events`). Ikke slette kalendere, ændre deling eller læse mail.
- **Hvor login sker:** Google sender kun svaret tilbage til `localhost`. Derfor logger man ind fra den bærbare
  gennem en SSH-tunnel til serveren (scriptet ordner det). Så ser browseren Family Hub som `localhost`.
- **Én konto er nok,** hvis de andre kalendere er delt med den. Man kan også forbinde flere konti;
  en kalender, der ses fra to konti, vises kun én gang.

## 1. Opret et Google Cloud-projekt

1. Gå til <https://console.cloud.google.com> og log ind med din Google-konto.
2. Vælg projektmenuen øverst → **Nyt projekt** → navn `Family Hub` → **Opret**.
3. **API'er og tjenester → Bibliotek** → søg efter **Google Calendar API** → **Aktivér**.

## 2. Samtykkeskærm (det, man ser ved login)

Under **Google Auth Platform** (hed tidligere »OAuth-samtykkeskærm«):

1. **Branding:** appnavn `Family Hub`, din e-mail som support- og udviklerkontakt.
2. **Målgruppe:** vælg **Ekstern**.
3. **Dataadgang** (valgfrit): tilføj `.../auth/calendar.calendarlist.readonly` og `.../auth/calendar.events`.
4. **Udgiv appen:** Målgruppe → **Udgiv app** (status »I produktion«).

> **Hvorfor udgive?** I status »Test« udløber adgangen efter 7 dage, og kalenderen holder op med at
> opdatere. En udgivet app skal *ikke* godkendes af Google, så længe det kun er jer, der bruger den –
> I får blot advarslen »Google har ikke bekræftet denne app« ved login. Tryk **Avanceret → Gå til Family Hub**.
> Det er jeres egen app, så det er i orden.

## 3. Opret klient-id og hent nøglefilen

1. **Google Auth Platform → Klienter → Opret klient**.
2. Programtype: **Computerprogram** (Desktop app). Navn: `Family Hub`.
3. **Download JSON** – filen hedder noget i retning af `client_secret_1234….json`.

Filen er en hemmelighed. Den må ikke ligge i git (`.gitignore` afviser `client_secret*.json` og `google-client.json`).

## 4. Læg nøglerne i `.env`

Åbn den downloadede fil i Notesblok. Den ser nogenlunde sådan ud:

```json
{"installed":{"client_id":"1234-abcd.apps.googleusercontent.com","project_id":"family-hub", … ,"client_secret":"GOCSPX-…", …}}
```

**Serveren (Docker)** – kopiér de to værdier (uden anførselstegn) ind i `.env` ved siden af `docker-compose.yml`:

```bash
ssh heine@homelab.local
nano ~/familyhub/.env
```

```ini
GOOGLE_CLIENT_ID=1234-abcd.apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=GOCSPX-…
```

Start containeren igen med de nye værdier, og slet den downloadede fil fra den bærbare bagefter:

```bash
cd ~/familyhub && docker compose up -d
```

`.env` er i `.gitignore` og `.dockerignore` og kommer hverken i git eller i imaget. Skabelonen er
[.env.example](../.env.example). Har du ikke `.env` endnu, se [README.md](../README.md#installation).

**Bærbar under udvikling** – vælg én af dem:

- Læg filen som `secrets\google-client.json` i projektmappen (`appsettings.Development.json` peger dertil med
  `FamilyHub:Calendar:Google:KeyFile`; mappen er i `.gitignore`), eller
- kopiér filen til `%LOCALAPPDATA%\FamilyHub\kalender\google-client.json` (fjern så `KeyFile` fra udviklingsindstillingerne), eller
- brug user-secrets:

  ```powershell
  dotnet user-secrets --project src/FamilyHub.Web init
  dotnet user-secrets --project src/FamilyHub.Web set "FamilyHub:Calendar:Google:ClientId" "…apps.googleusercontent.com"
  dotnet user-secrets --project src/FamilyHub.Web set "FamilyHub:Calendar:Google:ClientSecret" "…"
  ```

En nøglefil opdages af sig selv – ingen genstart.

## 5. Forbind en Google-konto

Fra projektmappen på den bærbare:

```powershell
.\deploy\forbind-google.ps1 -Server homelab.local -User heine
```

Scriptet åbner en SSH-tunnel og **Kalenderindstillinger** i din browser. Tryk **Forbind Google-konto** → log ind →
**Tillad**. Du kommer tilbage til indstillingerne, og kalenderne dukker op efter få sekunder. Tryk Enter i
terminalen, når kontoen står på listen. Køkkenskærmen viser kalenderne af sig selv.

Under udvikling: direkte på
<http://localhost:5080/kalender/indstillinger>.

## 6. Vælg kalendere og familiemedlemmer

Første gang gætter Family Hub selv:

- En kalender vises, hvis den også er slået til i Google Kalender.
- En kalender, der hedder som et familiemedlem (»Emma«, »Emmas fodbold«), eller din egen primære kalender
  (»heine@…«) får personens navn og farve. Resten bliver **Fælles** med deres egen farve.

Tryk på en kalender under **Kalenderindstillinger** for at vise/skjule den, vælge hvem den tilhører, eller
give en fælles kalender en anden farve. Dagvisningen får en kolonne pr. familiemedlem.

**Charlottes og børnenes kalendere:** Er de delt med din konto, ser du dem allerede. Ellers: i Google Kalender →
kalenderens **Indstillinger og deling → Del med bestemte personer** → tilføj din konto med
»Foretag ændringer i begivenheder«, hvis der skal kunne tilføjes aftaler fra skærmen.
Alternativt kan Charlotte forbinde sin egen konto.

## Fejlfinding

| Problem | Løsning |
|---|---|
| »… skal forbindes igen« | Adgangen er udløbet eller trukket tilbage (appen stod i »Test«, adgangskode skiftet, adgang fjernet). Kør `forbind-google.ps1`, og tryk **Forbind igen**. |
| »Kalenderen kunne ikke opdateres« | Google kan ikke nås. Skærmen viser den seneste kopi og prøver selv igen hvert 5. minut. |
| »Google-forbindelsen er ikke sat op endnu« | `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET` mangler i `.env`, eller containeren er ikke startet igen (`docker compose up -d`). Loggen siger mere: `docker compose logs`. |
| Knappen »Forbind« er grå | Family Hub er åbnet via et navn (fx `homelab.local`) i stedet for `localhost` – det gælder også køkkenskærmen. Brug `forbind-google.ps1`. |
| Tunnelen svarer ikke | Kører containeren (`docker compose ps`), og svarer den på port 8080 på serveren? |
| En kalender mangler | Er den delt med kontoen? Er den skjult i Google, vises den som »Skjult« – slå den til. Tryk **Opdatér nu**. |
| »Kun visning« | Kontoen må kun se kalenderen. Del den med »Foretag ændringer i begivenheder« for at kunne tilføje aftaler. |
| Fjern Family Hubs adgang helt | Fjern kontoen i Kalenderindstillinger, og fjern appen under <https://myaccount.google.com/permissions>. |

## Teknik (til udviklere)

- Login: OAuth 2.0 for installerede apps – autorisationskode + PKCE (S256) med loopback-redirect
  `http://localhost:<port>/kalender/google/callback`. Google tillader ikke kalenderadgang via »TV/enheds«-flowet,
  og http-redirects kun til localhost – deraf tunnelen. `GoogleOAuthClient`.
- Nøgler: `FamilyHub:Calendar:Google:ClientId`/`ClientSecret` (i Docker fra `.env` via `docker-compose.yml`) eller en
  nøglefil (`KeyFile`, ellers `kalender/google-client.json` i datamappen). `GoogleCredentialsProvider`.
- Adgang: refresh token krypteres med ASP.NET Data Protection (nøglerne ligger i datamappens `keys`) og gemmes i
  `kalender/google-konti.json` (rettigheder 600 på Linux). Access tokens holdes kun i hukommelsen.
- Data: `kalender/kalendere.json` (valg pr. kalender) og `kalender/cache.json` (kalendere + aftaler fra 2 måneder
  tilbage til 13 måneder frem). Cachen skrives kun, når noget har ændret sig.
- Synkronisering: `CalendarSyncWorker` (BackgroundService) hvert 5. minut og straks efter login, ændringer og
  »Opdatér nu«. Fejl vises med InfoBox, aldrig toasts. Ingen Google-SDK – kun HTTP + JSON (`GoogleCalendarApi`).
- Uden Google-nøgler kan kalenderen afprøves med demodata: `cd tools/ui-smoke; node demo-data.mjs`, og start appen med
  `$env:FamilyHub__DataDirectory = (Resolve-Path ./demo-data)`.
