#!/usr/bin/env bash
# Smoke test for the VPS staging stack (API, Redis, PostgreSQL).
# Usage: ./scripts/smoke-vps.sh
# Override the API URL: API_URL=http://localhost:8080 ./scripts/smoke-vps.sh
set -euo pipefail

cd "$(dirname "$0")/.."

pwsh -NoProfile -File ./scripts/smoke-test.ps1 \
  -ComposeFile docker-compose.vps.yaml \
  -EnvFile .env.vps \
  -ApiUrl "${API_URL:-https://gameserver-staging.bobak.me.uk}"
