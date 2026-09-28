#!/usr/bin/env bash
# Family Hub – bygger og starter serveren i Docker. Køres på serveren fra projektmappen (den med docker-compose.yml):
#
#   bash deploy/server/update.sh
#
# Bruges af deploy/publish-server.ps1, men kan også køres direkte, fx efter "git pull".
#   * første gang: opretter .env ud fra .env.example (udfyld den bagefter)
#   * docker compose up -d --build – data og .env røres ikke
#   * venter, til Family Hub svarer. Køkkenskærmen genindlæser selv.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

# Brug sudo, hvis brugeren ikke er i docker-gruppen.
DOCKER=(docker)
if ! docker info >/dev/null 2>&1; then
  DOCKER=(sudo docker)
fi

if [[ ! -f .env ]]; then
  cp .env.example .env
  chmod 600 .env
  echo "==> Oprettede .env ud fra .env.example i $(pwd)"
  echo "    Udfyld GOOGLE_CLIENT_ID og GOOGLE_CLIENT_SECRET (se docs/google-kalender.md), og kør scriptet igen."
fi

echo "==> Bygger og starter Family Hub"
"${DOCKER[@]}" compose up -d --build --remove-orphans

# Ryd gamle, ubrugte udgaver af imaget op, så disken ikke fyldes.
"${DOCKER[@]}" image prune -f --filter "label=com.docker.compose.project=familyhub" >/dev/null 2>&1 || true

echo -n "==> Venter på svar"
for _ in $(seq 1 60); do
  status="$("${DOCKER[@]}" inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' familyhub 2>/dev/null || true)"
  if [[ "$status" == "healthy" ]]; then
    echo " – Family Hub kører."
    exit 0
  fi
  echo -n "."
  sleep 2
done

echo
echo "Family Hub svarer ikke. Se loggen:  docker compose logs --tail 80 familyhub" >&2
exit 1
