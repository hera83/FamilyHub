#!/usr/bin/env bash
# Family Hub – gør en Raspberry Pi til køkkenskærm (kun kiosk; serveren kører i Docker et andet sted).
#
#   sudo bash install.sh <server-adresse> [bruger]
#   sudo bash install.sh http://homelab.local:5000 pi
#
# Raspberry Pi OS (64-bit, med skrivebord; Bookworm eller nyere). <bruger> er den bruger, der er logget ind på
# skærmen (standard: den der kører sudo). Scriptet kan køres igen uden skade – fx for at skifte serveradresse.
#   * installerer Chromium og curl
#   * gemmer serverens adresse i /etc/familyhub-kiosk.conf
#   * starter Chromium i kiosktilstand, når <bruger> logger ind
#   * slår skærmslukning og Raspberry Pi OS' eget skærmtastatur fra
#   * stopper en gammel Family Hub-server på Pi'en, hvis der er en (data bliver liggende)
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Kør med sudo: sudo bash install.sh <server-adresse> [bruger]" >&2
  exit 1
fi

SERVER_URL="${1:-}"
if [[ ! "$SERVER_URL" =~ ^https?:// ]]; then
  echo "Angiv serverens adresse, fx: sudo bash install.sh http://homelab.local:5000" >&2
  exit 1
fi
SERVER_URL="${SERVER_URL%/}"

KIOSK_USER="${2:-${SUDO_USER:-}}"
if [[ -z "$KIOSK_USER" || "$KIOSK_USER" == "root" ]]; then
  echo "Angiv brugeren, der er logget ind på skærmen: sudo bash install.sh $SERVER_URL <bruger>" >&2
  exit 1
fi

KIOSK_HOME="$(getent passwd "$KIOSK_USER" | cut -d: -f6)"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Installerer pakker"
apt-get update
BROWSER_PACKAGE=chromium
apt-cache show chromium >/dev/null 2>&1 || BROWSER_PACKAGE=chromium-browser
apt-get install -y --no-install-recommends "$BROWSER_PACKAGE" curl ca-certificates

echo "==> Serverens adresse: $SERVER_URL"
cat > /etc/familyhub-kiosk.conf <<EOF
# Family Hub-serveren, som kiosken åbner. Skift adressen og genstart Pi'en.
FAMILYHUB_URL=$SERVER_URL
EOF
chmod 644 /etc/familyhub-kiosk.conf

# Den tidligere opsætning kørte selve appen på Pi'en. Nu kører den i Docker – stop den gamle service.
if [[ -f /etc/systemd/system/familyhub.service ]]; then
  echo "==> Stopper den gamle Family Hub-server på Pi'en"
  systemctl disable --now familyhub || true
  rm -f /etc/systemd/system/familyhub.service
  systemctl daemon-reload
  if [[ -d /var/lib/familyhub ]]; then
    echo "   De gamle data ligger stadig i /var/lib/familyhub (flyt dem til serveren, hvis de skal bruges – se README.md)."
  fi
fi

echo "==> Kiosk ved login for $KIOSK_USER"
install -m 755 "$HERE/familyhub-kiosk.sh" /usr/local/bin/familyhub-kiosk

# XDG-autostart (virker på de fleste skriveborde) ...
AUTOSTART_DIR="$KIOSK_HOME/.config/autostart"
install -d -o "$KIOSK_USER" -g "$KIOSK_USER" "$AUTOSTART_DIR"
install -m 644 -o "$KIOSK_USER" -g "$KIOSK_USER" "$HERE/familyhub-kiosk.desktop" "$AUTOSTART_DIR/familyhub-kiosk.desktop"

# ... og labwc (standard i nyere Raspberry Pi OS). Scriptet sikrer selv, at kun én kiosk kører.
if [[ -d /etc/xdg/labwc || -d "$KIOSK_HOME/.config/labwc" ]]; then
  LABWC_DIR="$KIOSK_HOME/.config/labwc"
  install -d -o "$KIOSK_USER" -g "$KIOSK_USER" "$LABWC_DIR"
  if [[ ! -f "$LABWC_DIR/autostart" && -f /etc/xdg/labwc/autostart ]]; then
    install -m 644 -o "$KIOSK_USER" -g "$KIOSK_USER" /etc/xdg/labwc/autostart "$LABWC_DIR/autostart"
  fi
  touch "$LABWC_DIR/autostart"
  chown "$KIOSK_USER:$KIOSK_USER" "$LABWC_DIR/autostart"
  grep -q familyhub-kiosk "$LABWC_DIR/autostart" || echo "/usr/local/bin/familyhub-kiosk &" >> "$LABWC_DIR/autostart"
fi

# Raspberry Pi OS' eget skærmtastatur skal ikke dukke op oven i Family Hubs.
if [[ -f /etc/xdg/autostart/squeekboard.desktop ]]; then
  printf '[Desktop Entry]\nHidden=true\n' > "$AUTOSTART_DIR/squeekboard.desktop"
  chown "$KIOSK_USER:$KIOSK_USER" "$AUTOSTART_DIR/squeekboard.desktop"
fi

echo "==> Slår skærmslukning fra"
if command -v raspi-config >/dev/null 2>&1; then
  raspi-config nonint do_blanking 1 || echo "   (kunne ikke – sæt det manuelt i raspi-config → Display Options → Screen Blanking)"
fi

echo -n "==> Tjekker forbindelsen til serveren"
if curl -fsS --max-time 5 "$SERVER_URL/health" >/dev/null 2>&1; then
  echo " – Family Hub svarer."
else
  echo
  echo "   Family Hub svarer ikke på $SERVER_URL endnu. Kiosken venter selv, til den gør."
fi

cat <<EOF

Køkkenskærmen er sat op.
  * Kiosken åbner $SERVER_URL, når $KIOSK_USER logger ind – genstart Pi'en for at se den:  sudo reboot
  * Skift server:  kør install.sh igen med den nye adresse (eller ret /etc/familyhub-kiosk.conf) og genstart.
EOF
