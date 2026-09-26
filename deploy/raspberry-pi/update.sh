#!/usr/bin/env bash
# Family Hub – lægger en ny version på plads og genstarter servicen.
#
#   sudo bash update.sh
#
# Bruger de publicerede filer i mappen over denne deploy-mappe. Data i /var/lib/familyhub røres ikke.
# Køkkenskærmen genindlæser selv, når den nye version svarer.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Kør med sudo: sudo bash update.sh" >&2
  exit 1
fi

SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_DIR=/opt/familyhub
HEALTH_URL="${FAMILYHUB_HEALTH_URL:-http://localhost:5000/health}"

if [[ ! -f "$SOURCE/FamilyHub.Web" ]]; then
  echo "Fandt ikke FamilyHub.Web i $SOURCE – er pakken bygget med publish-pi.ps1?" >&2
  exit 1
fi

echo "==> Kopierer ny version til $APP_DIR"
install -d -m 755 "$APP_DIR"
rsync -a --delete "$SOURCE/" "$APP_DIR/"
chown -R root:root "$APP_DIR"
chmod 755 "$APP_DIR/FamilyHub.Web"

echo "==> Genstarter Family Hub"
systemctl restart familyhub

echo -n "==> Venter på svar"
for _ in $(seq 1 60); do
  if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then
    echo " – Family Hub kører."
    exit 0
  fi
  echo -n "."
  sleep 1
done

echo
echo "Family Hub svarer ikke. Se loggen:  journalctl -u familyhub -n 80 --no-pager" >&2
exit 1
