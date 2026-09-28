# Family Hub

Familiens køkkenskærm – kalender, madplan og mere. Serveren kører i **Docker** i homelab'en, og en
**Raspberry Pi** med 19" touchskærm viser den i Chromium i kiosktilstand.
Bygget i C# med .NET 10 (Blazor Server).

**Status:** Fundamentet er på plads: app-skal med navigation, designsystem, dansk skærmtastatur med
touch-detektion, toasts og informationsbokse, indstillinger pr. skærm og for familien, nattilstand og
opsætning til Docker og Raspberry Pi. *Kalender* viser familiens Google-kalendere (dag, uge, måned) og kan tilføje aftaler.
*Madplan* er et skelet, klar til at blive bygget.

```
Homelab-server (Docker)                          Raspberry Pi 5 + touchskærm
┌──────────────────────────────┐                 ┌──────────────────────────────┐
│ container "familyhub"        │  http :8080     │ Chromium i kiosktilstand     │
│  Blazor Server (.NET 10)     │ ◄────────────── │  (kun visning og touch)      │
│  volumen: /data              │   hjemmenettet  │                              │
└──────────────────────────────┘                 └──────────────────────────────┘
        ▲ .env: port, tidszone, Google-nøgler
```

## Installation

Det skal du bruge:

- **En server** med Docker og compose-plugin (`docker compose version`) og SSH-adgang. x64 eller arm64.
- **En Raspberry Pi** med Raspberry Pi OS (64-bit, med skrivebord), touchskærm og SSH slået til.
  Se [docs/raspberry-pi.md](docs/raspberry-pi.md), hvis Pi'en ikke er sat op endnu.
- **Din bærbare** med .NET 10 SDK og OpenSSH (følger med Windows). Herfra sendes og opdateres alt.

Eksemplerne bruger `homelab.local` som serveren og `familyhub.local` som Pi'en. Ret dem til dine egne navne.

### 1. Start serveren

Fra projektmappen på den bærbare:

```powershell
.\deploy\publish-server.ps1 -Server homelab.local -User heine
```

Scriptet kører alle tests, sender kildekoden til `~/familyhub` på serveren (aldrig `.env`, nøgler eller bin/obj),
bygger imaget dér og starter containeren. Første gang opretter det `~/familyhub/.env` ud fra
[.env.example](.env.example).

Tjek, at den kører: åbn <http://homelab.local:8080> i browseren.

<details>
<summary>Uden scriptet (fx med git på serveren)</summary>

```bash
git clone <repo> ~/familyhub && cd ~/familyhub
cp .env.example .env && nano .env
docker compose up -d --build        # eller: bash deploy/server/update.sh
```

</details>

### 2. Udfyld `.env`

Alle indstillinger og hemmeligheder står i `.env` på serveren, ved siden af `docker-compose.yml`:

```bash
ssh heine@homelab.local
nano ~/familyhub/.env
```

| Variabel | Standard | Betydning |
|---|---|---|
| `FAMILYHUB_TIMEZONE` | `Europe/Copenhagen` | Tidszone for ur, hilsner og nattilstand. |
| `FAMILYHUB_DATA` | `familyhub-data` | Docker-volumen eller en sti på serveren (se [Data og backup](#data-og-backup)). |
| `GOOGLE_CLIENT_ID` | – | `client_id` fra Googles nøglefil. |
| `GOOGLE_CLIENT_SECRET` | – | `client_secret` fra Googles nøglefil. |

Porten kan ikke ændres: Family Hub svarer altid på **port 8080** på alle serverens netkort.

Google-værdierne får du ved at følge trin 1–3 i [docs/google-kalender.md](docs/google-kalender.md).
Kalenderen virker ikke uden dem, men resten af Family Hub gør.

Genstart containeren med de nye værdier:

```bash
cd ~/familyhub && docker compose up -d
```

`.env` er i `.gitignore` og `.dockerignore`. Den kommer hverken i git eller i imaget.

### 3. Forbind Google-kontoen

Google sender kun login-svaret tilbage til en https-adresse eller `localhost`. Har Family Hub sit eget domæne med
https (reverse proxy), så åbn `https://<domæne>/kalender/indstillinger` og tryk **Forbind Google-konto** – se
[docs/google-kalender.md](docs/google-kalender.md). Ellers logger du ind fra den bærbare gennem en SSH-tunnel:

```powershell
.\deploy\forbind-google.ps1 -Server homelab.local -User heine
```

Browseren åbner Kalenderindstillinger. Tryk **Forbind Google-konto**, log ind, og tryk Enter i terminalen.

### 4. Gør Pi'en til køkkenskærm

```powershell
.\deploy\opsaet-kiosk.ps1 -ServerUrl http://homelab.local:8080 -PiHost familyhub.local -PiUser pi
ssh pi@familyhub.local sudo reboot
```

Pi'en installerer Chromium og starter Family Hub i fuld skærm, når den logger ind. Er serveren nede, venter den,
til serveren svarer igen. Har Pi'en tidligere kørt Family Hub selv, stoppes den gamle service (data bliver liggende).

Indstil så skærmen på selve skærmen under **Indstillinger → Denne skærm** (tastatur, tema, visningsstørrelse).

### 5. Begræns adgangen

Family Hub har endnu ingen login. Alle, der kan nå porten, kan se og ændre. Sørg for, at kun Pi'en
(og evt. familiens telefoner) kan nå den:

- Hold gæste-wifi adskilt fra hjemmenettet i routeren, eller læg serveren og Pi'en i eget VLAN.
- Bloker port 8080 udefra i routeren – den skal aldrig videresendes til internettet.

Bemærk: Docker åbner porte uden om `ufw` – en `ufw`-regel alene beskytter ikke porten.
Bruger du en reverse proxy foran, skal WebSockets være slået til.

## Opdatering

```powershell
.\deploy\publish-server.ps1 -Server homelab.local -User heine
```

Køkkenskærmen viser kort "Et øjeblik …" og genindlæser af sig selv, når den nye version kører.
`.env` og data røres ikke.

## Data og backup

Alt, hvad Family Hub gemmer, ligger i `/data` i containeren: husstand, kalender, senere databaser og
**`keys`**. `keys` krypterer den gemte Google-adgang. Mistes den, skal Google-kontiene forbindes igen.

- **Docker-volumen** (standard): ligger under `/var/lib/docker/volumes/familyhub_familyhub-data/_data`. Backup:

  ```bash
  docker run --rm -v familyhub_familyhub-data:/data -v "$PWD":/backup alpine tar -czf /backup/familyhub-data.tar.gz -C /data .
  ```

- **Mappe på serveren:** sæt fx `FAMILYHUB_DATA=/srv/familyhub` i `.env`, og giv containerens bruger adgang:
  `sudo mkdir -p /srv/familyhub && sudo chown -R 1654:1654 /srv/familyhub`. Brug en lokal disk, ikke en NFS/SMB-share.

**Flyt data fra den gamle Pi-installation:** kopiér `/var/lib/familyhub` fra Pi'en til datamappen på serveren
(inkl. `keys`), sæt ejer til `1654:1654`, og kør `docker compose up -d`.

## Nyttige kommandoer på serveren

```bash
cd ~/familyhub
docker compose ps                   # kører den? (healthy)
docker compose logs -f              # følg loggen
docker compose restart              # genstart (skærmen genindlæser selv)
docker compose down                 # stop (data bliver)
```

## Udvikling

Kræver .NET 10 SDK.

```powershell
dotnet run --project src/FamilyHub.Web     # åbn http://localhost:5080
dotnet test                                # kør alle tests
```

Google-nøgler under udvikling: læg nøglefilen som `secrets\google-client.json` (se [docs/google-kalender.md](docs/google-kalender.md)).
Gå til **Indstillinger → Komponentoversigt** for at se og afprøve alle byggeklodserne –
også skærmtastaturet, hvis din skærm har touch.

## Struktur

```
src/
  FamilyHub.Core/        Logik uden UI: tid, toasts, husstand, lagring, tastaturpolitik
  FamilyHub.UI/          Designsystemet: komponenter, skærmtastatur, CSS-tokens, modul-kontrakt
  FamilyHub.Web/         App-skallen: layout, navigation, forside, indstillinger
  Modules/
    FamilyHub.Modules.Calendar/   Kalender (Google Kalender: dag, uge, måned)
    FamilyHub.Modules.MealPlan/   Madplan og indkøbsliste (skelet)
tests/FamilyHub.Tests/   Unit-, komponent- og opstartstests
tools/ui-smoke/          Browser-test af touch og skærmtastatur (Playwright + Chrome)
Dockerfile               Imaget (byg + kør)
docker-compose.yml       Serveren; indstillinger fra .env (skabelon: .env.example)
deploy/
  publish-server.ps1     Test, send og start på serveren
  forbind-google.ps1     Google-login gennem SSH-tunnel
  opsaet-kiosk.ps1       Gør en Raspberry Pi til køkkenskærm
  server/  kiosk/        Scripts, der køres på serveren og på Pi'en
docs/                    Arkitektur, standarder, Pi- og Google-vejledning
```

## Dokumentation

- [Arkitektur](docs/arkitektur.md) – hvordan det hænger sammen, og hvorfor
- [Designstandarder](docs/standarder/README.md) – layout, komponenter, feedback, input og tastatur, tekst, nye menuer
- [Raspberry Pi](docs/raspberry-pi.md) – køkkenskærmen: opsætning og fejlfinding
- [Google Kalender](docs/google-kalender.md) – opsætning af kalenderen
