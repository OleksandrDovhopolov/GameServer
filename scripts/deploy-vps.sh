#!/usr/bin/env bash
# Deploys a GameServer API image from GHCR to the VPS staging stack, then runs the smoke test.
# Usage: ./scripts/deploy-vps.sh              (latest "staging" image)
#        ./scripts/deploy-vps.sh sha-1a2b3c4  (specific build, e.g. rollback)
set -euo pipefail

cd "$(dirname "$0")/.."

export API_TAG="${1:-staging}"
compose=(docker compose --env-file .env.vps -f docker-compose.vps.yaml)

echo "Deploying ghcr.io/oleksandrdovhopolov/gameserver-api:${API_TAG}"
"${compose[@]}" pull api
"${compose[@]}" up -d

echo "Waiting for API to start..."
for _ in {1..30}; do
  if curl -fsS http://127.0.0.1:8080/ping >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

./scripts/smoke-vps.sh

# Remove images that lost their tag after the pull, to keep the VPS disk small.
docker image prune -f >/dev/null
