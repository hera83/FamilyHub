# Køkkenskærmen: Raspberry Pi i kiosktilstand

Målet: Pi'en starter direkte i Family Hub i fuld skærm – uden mus og tastatur. Selve Family Hub kører i Docker
på serveren (se [README.md](../README.md#installation)). Pi'en viser den kun i Chromium.

## Det skal du bruge

- Raspberry Pi 5 med officiel strømforsyning (27 W). Pi'en laver kun visning, så en mindre model kan også bruges.
- 19" touchskærm: HDMI til billede **og** USB til touch.
- Lagring: et godt microSD-kort (A2) er nok – Pi'en gemmer kun browserens egne indstillinger.
- **Netværk: helst kabel.** Blazor Server holder en fast forbindelse til serveren; ustabil wifi giver "Genforbinder …".
- Family Hub skal køre på serveren først: <http://homelab.local:8080> skal kunne åbnes.

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

Tip: Slip for at skrive adgangskode hver gang ved at oprette en SSH-nøgle:

```powershell
ssh-keygen -t ed25519            # tryk Enter til alle spørgsmål
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh pi@familyhub.local "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

## 3. Sæt kiosken op

Fra projektmappen på den bærbare:

```powershell
.\deploy\opsaet-kiosk.ps1 -ServerUrl http://homelab.local:8080 -PiHost familyhub.local -PiUser pi
ssh pi@familyhub.local sudo reboot
```

Scriptet kopierer `deploy/kiosk` til Pi'en og kører `install.sh`, som:

- installerer Chromium og curl
- gemmer serverens adresse i `/etc/familyhub-kiosk.conf`
- starter Chromium i kiosktilstand, når `pi` logger ind – og venter først, til serveren svarer
- slår skærmslukning fra og holder Raspberry Pi OS' eget skærmtastatur væk
- stopper en gammel Family Hub-server på Pi'en, hvis Pi'en tidligere kørte appen selv (data i `/var/lib/familyhub`
  bliver liggende – se [README.md](../README.md#data-og-backup) for at flytte dem til serveren)

**Skift server:** kør scriptet igen med den nye adresse, og genstart Pi'en.

## 4. Indstil skærmen

På køkkenskærmen: **Indstillinger → Denne skærm**

- Skærmtastatur: bør vise "Touchskærm fundet". Gør den ikke det, så vælg **Altid**.
- Tilbage til forsiden: fx **5 min**.
- Tema: **Efter tidspunkt** (mørkt fra 21 til 6) eller fast.
- Visningsstørrelse: tilpas efter hvor langt væk, man står.

Indstillingerne gemmes i browseren på Pi'en for netop den adresse. Skifter du serveradresse, skal de sættes igen.

## Opdateringer

Pi'en skal ikke opdateres, når Family Hub opdateres – det sker på serveren (`deploy/publish-server.ps1`).
Køkkenskærmen viser kort "Et øjeblik …" og genindlæser af sig selv. Opdatér Raspberry Pi OS og Chromium en gang
imellem: `ssh pi@familyhub.local "sudo apt update && sudo apt full-upgrade -y && sudo reboot"`.

## Fejlfinding

| Problem | Løsning |
|---|---|
| Skærmen viser kun skrivebordet | Kiosken venter på serveren. Tjek fra Pi'en: `curl http://homelab.local:8080/health`. Kører containeren (`docker compose ps` på serveren)? |
| "Genforbinder …" dukker tit op | Forbindelsen mellem Pi og server er ustabil – brug kabel. En reverse proxy skal tillade WebSockets. |
| Forkert server | `cat /etc/familyhub-kiosk.conf` – kør `opsaet-kiosk.ps1` igen med den rigtige adresse. |
| `homelab.local` kan ikke findes | Brug serverens IP-adresse i stedet, fx `-ServerUrl http://192.168.1.10:8080`. |
| Touch bliver ikke genkendt | Tjek USB-kablet til skærmen. Vælg **Altid** under Skærmtastatur. `libinput list-devices` viser touch-enheder. |
| Raspberry Pi OS' eget tastatur dukker op | `sudo raspi-config` → Display Options → On-screen Keyboard → Disabled |
| Skærmen går i sort | `sudo raspi-config` → Display Options → Screen Blanking → No |
| Skærmen skal roteres | Skrivebordets "Screen Configuration", eller `wlr-randr` |
| Musemarkøren står midt på skærmen | Den forsvinder efter første tryk på skærmen. |
| Kiosken starter ikke | Start den manuelt i en terminal på Pi'en: `familyhub-kiosk` og se beskeden. |

## Nyttigt at vide

- **Kiosk-profilen** (skærmens egne indstillinger) ligger i `~/.config/familyhub-kiosk`. Slettes den, starter skærmen
  med standardindstillinger.
- **Google-login** sker ikke på skærmen, men fra den bærbare med `deploy/forbind-google.ps1` – se
  [google-kalender.md](google-kalender.md).
- **Flere skærme:** en ekstra Pi (eller en tablet) sættes op på samme måde og åbner den samme server. Hver skærm har
  sine egne indstillinger; familiens data er fælles.
