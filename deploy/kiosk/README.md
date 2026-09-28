# Family Hub-kiosk på Raspberry Pi

Pi'en viser kun Family Hub i fuld skærm. Selve appen kører i Docker på serveren (se [README.md](../../README.md)).
Filerne her bliver lagt på Pi'en af `deploy/opsaet-kiosk.ps1`. Hele vejledningen står i
[docs/raspberry-pi.md](../../docs/raspberry-pi.md).

| Fil | Hvad den gør |
|---|---|
| `install.sh` | Engangsopsætning: Chromium, serverens adresse, kiosk ved login, ingen skærmslukning. |
| `familyhub-kiosk.sh` | Venter, til serveren svarer, og starter Chromium i fuldskærm (genstarter den, hvis den lukkes). |
| `familyhub-kiosk.desktop` | Autostart af kiosken, når skærmens bruger logger ind. |

## Nyttigt på Pi'en

```bash
cat /etc/familyhub-kiosk.conf       # hvilken server åbner kiosken?
familyhub-kiosk                      # start kiosken manuelt og se eventuelle fejl
curl http://homelab.local:8080/health  # kan Pi'en nå serveren?
```
