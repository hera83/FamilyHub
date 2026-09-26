#!/usr/bin/env bash
# Family Hub – engangsopsætning på Raspberry Pi OS (64-bit, med skrivebord; Bookworm eller nyere).
#
#   sudo bash install.sh <bruger>
#
# <bruger> er den bruger, der er logget ind på skærmen (standard: den der kører sudo).
# Scriptet kan køres igen uden skade.
#   * installerer Chromium, ICU (dansk dato/tal-format), curl og rsync
#   * opretter systembrugeren "familyhub" og systemd-servicen "familyhub"
#   * starter Chromium i kiosktilstand, når <bruger> logger ind
#   * slår skærmslukning fra
#   * installerer selve appen (filerne ved siden af deploy-mappen) via update.sh
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Kør med sudo: sudo bash install.sh <bruger>" >&2
  exit 1
fi

KIOSK_USER="${1:-${SUDO_USER:-}}"
if [[ -z "$KIOSK_USER" || "$KIOSK_USER" == "root" ]]; then
  echo "Angiv brugeren, der er logget ind på skærmen: sudo bash install.sh <bruger>" >&2
  exit 1
fi

KIOSK_HOME="$(getent passwd "$KIOSK_USER" | cut -d: -f6)"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Installerer pakker"
apt-get update
BROWSER_PACKAGE=chromium
apt-cache show chromium >/dev/null 2>&1 || BROWSER_PACKAGE=chromium-browser
ICU_PACKAGE="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1)"
apt-get install -y --no-install-recommends "$BROWSER_PACKAGE" "$ICU_PACKAGE" curl rsync ca-certificates

echo "==> Opretter systembrugeren familyhub"
if ! id familyhub >/dev/null 2>&1; then
  useradd --system --no-create-home --shell /usr/sbin/nologin familyhub
fi

echo "==> Installerer systemd-servicen"
install -m 644 "$HERE/familyhub.service" /etc/systemd/system/familyhub.service
systemctl daemon-reload
systemctl enable familyhub

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

echo "==> Installerer appen"
bash "$HERE/update.sh"

cat <<EOF

Family Hub er installeret.
  * Servicen:  systemctl status familyhub      (log: journalctl -u familyhub -f)
  * Kiosken starter, når $KIOSK_USER logger ind – genstart Pi'en for at se den:  sudo reboot
  * Opdateringer sendes fra udviklingsmaskinen med  deploy/publish-pi.ps1
EOF
