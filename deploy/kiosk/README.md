# Family Hub på Raspberry Pi

Filerne her bliver lagt på Pi'en af `deploy/publish-pi.ps1`. Den fulde vejledning står i
[docs/raspberry-pi.md](../../docs/raspberry-pi.md).

| Fil | Hvad den gør |
|---|---|
| `install.sh` | Engangsopsætning: pakker, systembruger, service, kiosk ved login, ingen skærmslukning. |
| `update.sh` | Lægger en ny version i `/opt/familyhub` og genstarter servicen. |
| `familyhub.service` | systemd-service – kører appen som brugeren `familyhub` på `http://localhost:5000`. |
| `familyhub-kiosk.sh` | Venter på appen og starter Chromium i fuldskærm (genstarter den, hvis den lukkes). |
| `familyhub-kiosk.desktop` | Autostart af kiosken, når skærmens bruger logger ind. |

## Nyttige kommandoer på Pi'en

```bash
systemctl status familyhub          # kører den?
journalctl -u familyhub -f          # følg loggen
sudo systemctl restart familyhub    # genstart appen (skærmen genindlæser selv)
ls /var/lib/familyhub               # data: household.json, nøgler …
```
