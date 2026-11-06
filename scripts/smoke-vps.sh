#!/usr/bin/env bash
# Checks the public API and both stateful dependencies of the VPS staging stack.
# Usage: ./scripts/smoke-vps.sh
# Override the API URL: API_URL=http://127.0.0.1:8080 ./scripts/smoke-vps.sh
set -euo pipefail

cd "$(dirname "$0")/.."

compose=(docker compose --env-file .env.vps -f docker-compose.vps.yaml)
api_url="${API_URL:-https://gameserver-staging.bobak.me.uk}"
api_url="${api_url%/}"

fail() {
  printf '[FAIL] %s\n' "$1" >&2
  exit 1
}

api_response="$(curl --fail --silent --show-error --max-time 10 "${api_url}/ping")" || fail "API /ping is unreachable"
[[ "$api_response" == pong ]] || fail "API /ping returned an unexpected response"
printf '[OK] API /ping returned pong\n'

redis_response="$("${compose[@]}" exec -T redis redis-cli ping)" || fail "Redis is not running or is unreachable"
[[ "$redis_response" == PONG ]] || fail "Redis did not return PONG"
printf '[OK] Redis returned PONG\n'

postgres_response="$("${compose[@]}" exec -T postgres sh -ec 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "select 1;"')" || fail "PostgreSQL query failed"
[[ "$postgres_response" == 1 ]] || fail "PostgreSQL did not return 1"
printf '[OK] PostgreSQL returned 1\n'

printf 'VPS smoke test passed.\n'
