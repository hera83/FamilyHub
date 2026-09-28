#!/usr/bin/env bash
# Family Hub – starter Chromium i fuldskærm (kiosk), når serveren svarer.
# Startes automatisk ved login. Genstarter browseren, hvis den lukkes eller går ned.
#
# Adressen på serveren står i /etc/familyhub-kiosk.conf (FAMILYHUB_URL=http://homelab:8080),
# som install.sh skriver. Den kan også gives som miljøvariabel.

CONFIG=/etc/familyhub-kiosk.conf
# shellcheck source=/dev/null
[[ -z "${FAMILYHUB_URL:-}" && -f "$CONFIG" ]] && source "$CONFIG"
URL="${FAMILYHUB_URL:-http://localhost:8080}"
URL="${URL%/}"
PROFILE="$HOME/.config/familyhub-kiosk"   # egen profil: bevarer skærmens indstillinger (localStorage)

# Kun én kiosk ad gangen, selvom flere autostart-mekanismer skulle starte scriptet.
exec 9>"${XDG_RUNTIME_DIR:-/tmp}/familyhub-kiosk.lock"
flock -n 9 || exit 0

BROWSER="$(command -v chromium || command -v chromium-browser)"
if [[ -z "$BROWSER" ]]; then
  echo "Chromium er ikke installeret." >&2
  exit 1
fi

# Serveren kører et andet sted (homelab'en) og kan være længere om at starte end Pi'en – eller være nede.
# Vent, til den svarer, så Chromium aldrig står med en fejlside. Appen genforbinder selv, når den først er åbnet.
wait_for_server() {
  until curl -fsS --max-time 3 "$URL/health" >/dev/null 2>&1; do
    sleep 2
  done
}

while true; do
  wait_for_server

  # Undgå "Gendan sider?"-boblen efter strømsvigt.
  PREFS="$PROFILE/Default/Preferences"
  if [[ -f "$PREFS" ]]; then
    sed -i 's/"exited_cleanly":false/"exited_cleanly":true/; s/"exit_type":"[^"]*"/"exit_type":"Normal"/' "$PREFS"
  fi

  "$BROWSER" \
    --user-data-dir="$PROFILE" \
    --kiosk \
    --noerrdialogs \
    --disable-infobars \
    --no-first-run \
    --disable-session-crashed-bubble \
    --disable-features=Translate,TranslateUI \
    --overscroll-history-navigation=0 \
    --disable-pinch \
    --touch-events=enabled \
    --password-store=basic \
    --check-for-update-interval=31536000 \
    "$URL"

  sleep 3
done
