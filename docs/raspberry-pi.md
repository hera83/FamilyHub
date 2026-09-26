# Opsætning på Raspberry Pi

Målet: Pi'en starter direkte i Family Hub i fuld skærm – uden mus og tastatur – og kan opdateres fra
din bærbare med én kommando.

## Det skal du bruge

- Raspberry Pi 5 (16 GB) med officiel strømforsyning (27 W).
- 19" touchskærm: HDMI til billede **og** USB til touch.
- Lagring: et godt microSD-kort (A2) – eller bedre og mere holdbart: en NVMe-SSD via M.2-HAT.
- Din Windows-bærbare med .NET 10 SDK (til at bygge og sende opdateringer).

## 1. Installér Raspberry Pi OS

1. Brug **Raspberry Pi Imager** og vælg *Raspberry Pi OS (64-bit)* – udgaven **med skrivebord**.
2. Tryk på tandhjulet/"Rediger indstillinger" før skrivning:
   - Værtsnavn: `familyhub`
   - Brugernavn og adgangskode (fx `pi`) – det er den bruger, der logger ind på skærmen
   - Wi-Fi (eller brug kabel)
   - Landestandard: tidszone `Europe/Copenhagen`, tastatur `dk`
   - **Slå SSH til**
3. Start Pi'en med skærmen tilsluttet og lad den køre første opstart færdig.

## 2. Forbind fra den bærbare

```powershell
ssh pi@familyhub.local
```

Tip: Slip for at skrive adgangskode ved hver opdatering ved at oprette en SSH-nøgle:

```powershell
ssh-keygen -t ed25519            # tryk Enter til alle spørgsmål
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh pi@familyhub.local "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

## 3. Installér Family Hub (første gang)

Fra projektmappen på den bærbare:

```powershell
.\deploy\publish-pi.ps1 -PiHost familyhub.local -PiUser pi -Install
```

Scriptet kører alle tests, bygger en selvstændig linux-arm64-udgave (Pi'en behøver ikke .NET), kopierer den
til Pi'en og kører `install.sh`, som:

- installerer Chromium, ICU (dansk dato/tal-format), curl og rsync
- opretter systembrugeren `familyhub` og servicen `familyhub` (starter automatisk, genstarter ved fejl)
- starter Chromium i kiosktilstand, når `pi` logger ind
- slår skærmslukning fra og holder Raspberry Pi OS' eget skærmtastatur væk

Genstart derefter Pi'en: `ssh pi@familyhub.local sudo reboot`.

## 4. Indstil skærmen

På køkkenskærmen: **Indstillinger → Denne skærm**

- Skærmtastatur: bør vise "Touchskærm fundet". Gør den ikke det, så vælg **Altid**.
- Tilbage til forsiden: fx **5 min**.
- Tema: **Efter tidspunkt** (mørkt fra 21 til 6) eller fast.
- Visningsstørrelse: tilpas efter hvor langt væk, man står.

## 5. Opdateringer

```powershell
.\deploy\publish-pi.ps1 -PiHost familyhub.local
```

Køkkenskærmen viser kort "Et øjeblik …" og genindlæser af sig selv, når den nye version kører.

## Fejlfinding

| Problem | Løsning |
|---|---|
| Kører appen? | `ssh pi@familyhub.local systemctl status familyhub` |
| Se loggen | `ssh pi@familyhub.local journalctl -u familyhub -f` |
| Touch bliver ikke genkendt | Tjek USB-kablet til skærmen. Vælg **Altid** under Skærmtastatur. `libinput list-devices` viser touch-enheder. |
| Raspberry Pi OS' eget tastatur dukker op | `sudo raspi-config` → Display Options → On-screen Keyboard → Disabled |
| Skærmen går i sort | `sudo raspi-config` → Display Options → Screen Blanking → No |
| Skærmen skal roteres | Skrivebordets "Screen Configuration", eller `wlr-randr` |
| Musemarkøren står midt på skærmen | Den forsvinder efter første tryk på skærmen. |
| Kiosken starter ikke | Start den manuelt i en terminal på Pi'en: `familyhub-kiosk` og se beskeden. |

## Nyttigt at vide

- **Data** ligger i `/var/lib/familyhub` (husstand, nøgler, senere databaser). Tag backup af den mappe.
- **Appen** ligger i `/opt/familyhub` og overskrives ved hver opdatering.
- **Åbn fra telefonen:** ret `ASPNETCORE_URLS=http://localhost:5000` til `http://0.0.0.0:5000` i
  `/etc/systemd/system/familyhub.service`, kør `sudo systemctl daemon-reload && sudo systemctl restart familyhub`,
  og åbn `http://familyhub.local:5000`. Bemærk: der er endnu ingen login, så alle på netværket kan se og ændre.
- **Kiosk-profilen** (skærmens egne indstillinger) ligger i `~/.config/familyhub-kiosk`. Slettes den, starter skærmen med standardindstillinger.
