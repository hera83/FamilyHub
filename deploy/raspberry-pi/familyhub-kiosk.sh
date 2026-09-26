#!/usr/bin/env bash
# Family Hub – starter Chromium i fuldskærm (kiosk), når serveren svarer.
# Startes automatisk ved login. Genstarter browseren, hvis den lukkes eller går ned.

URL="${FAMILYHUB_URL:-http://localhost:5000}"
PROFILE="$HOME/.config/familyhub-kiosk"   # egen profil: bevarer skærmens indstillinger (localStorage)

# Kun én kiosk ad gangen, selvom flere autostart-mekanismer skulle starte scriptet.
exec 9>"${XDG_RUNTIME_DIR:-/tmp}/familyhub-kiosk.lock"
flock -n 9 || exit 0

BROWSER="$(command -v chromium || command -v chromium-browser)"
if [[ -z "$BROWSER" ]]; then
  echo "Chromium er ikke installeret." >&2
  exit 1
fi

# Vent på Family Hub (op til to minutter efter opstart), så Chromium ikke viser en fejlside.
for _ in $(seq 1 120); do
  curl -fsS "$URL/health" >/dev/null 2>&1 && break
  sleep 1
done

while true; do
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
