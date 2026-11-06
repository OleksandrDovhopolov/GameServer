#!/usr/bin/env bash
# Deploys one API image to VPS staging and restores the previous image on failure.
# Usage: ./scripts/deploy-vps.sh sha-1a2b3c4
#        ./scripts/deploy-vps.sh rollback-last-good
#        ./scripts/deploy-vps.sh staging  (manual use only; this tag moves)
set -euo pipefail

cd "$(dirname "$0")/.."
image=ghcr.io/oleksandrdovhopolov/gameserver-api
target_tag="${1:-}"
if [[ $# -ne 1 || ! "$target_tag" =~ ^(sha-[0-9a-f]{7,40}|staging|rollback-last-good)$ ]]; then
  echo 'Usage: ./scripts/deploy-vps.sh sha-<commit> | staging | rollback-last-good' >&2
  exit 2
fi

[[ -f .env.vps ]] || { echo 'Missing .env.vps' >&2; exit 1; }
compose=(docker compose --env-file .env.vps -f docker-compose.vps.yaml)
export API_TAG="$target_tag"
"${compose[@]}" config -q

# This lock also covers manual deploys; GitHub Actions concurrency covers workflow runs.
mkdir -p -m 700 "$HOME/.cache/gameserver"
exec 9>"$HOME/.cache/gameserver/deploy.lock"
flock -n 9 || { echo 'Another VPS deployment is running' >&2; exit 1; }

wait_for_api() {
  for ((attempt = 1; attempt <= 30; attempt++)); do
    if curl --fail --silent --max-time 2 http://127.0.0.1:8080/ping >/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

previous_container="$("${compose[@]}" ps -a -q api)"
previous_image_id=''
previous_image_ref=''
rollback_tag=rollback-last-good
if [[ -n "$previous_container" ]]; then
  previous_image_id="$(docker inspect --format '{{.Image}}' "$previous_container")"
  previous_image_ref="$(docker inspect --format '{{.Config.Image}}' "$previous_container")"
fi

rollback() {
  if [[ -z "$previous_image_id" ]]; then
    echo 'No previous API container exists; automatic rollback is unavailable' >&2
    return 1
  fi
  echo "Restoring previous API image: ${previous_image_ref} (${previous_image_id})" >&2
  export API_TAG="$rollback_tag"
  "${compose[@]}" up -d --no-deps --pull never api || return 1
  wait_for_api || return 1
  ./scripts/smoke-vps.sh || return 1
  local restored_container restored_image_id
  restored_container="$("${compose[@]}" ps -q api)"
  restored_image_id="$(docker inspect --format '{{.Image}}' "$restored_container")"
  [[ "$restored_image_id" == "$previous_image_id" ]] || return 1
  echo "Rollback succeeded: ${previous_image_ref} (${restored_image_id})" >&2
}

fail_after_change() {
  echo "Deployment of ${image}:${target_tag} failed" >&2
  if ! rollback; then
    echo 'ROLLBACK FAILED: inspect the API container and restore it manually' >&2
  fi
  exit 1
}

if [[ "$target_tag" == rollback-last-good ]]; then
  docker image inspect "${image}:${target_tag}" >/dev/null || {
    echo 'No locally saved rollback-last-good image exists' >&2
    exit 1
  }
  if [[ -n "$previous_image_id" ]]; then
    rollback_tag=rollback-before-manual
    docker image tag "$previous_image_id" "${image}:${rollback_tag}"
  fi
else
  if [[ -n "$previous_image_id" ]]; then
    docker image tag "$previous_image_id" "${image}:rollback-last-good"
    echo "Saved current image for rollback: ${previous_image_ref} (${previous_image_id})"
  fi
  echo "Pulling ${image}:${target_tag}"
  "${compose[@]}" pull api || { echo 'Pull failed; current API was not changed' >&2; exit 1; }
fi

echo "Starting ${image}:${target_tag}"
"${compose[@]}" up -d --no-deps --pull never api || fail_after_change
wait_for_api || fail_after_change
./scripts/smoke-vps.sh || fail_after_change

running_container="$("${compose[@]}" ps -q api)"
running_image_id="$(docker inspect --format '{{.Image}}' "$running_container")"
expected_image_id="$(docker image inspect --format '{{.Id}}' "${image}:${target_tag}")"
[[ "$running_image_id" == "$expected_image_id" ]] || fail_after_change
if [[ "${DEPLOY_REHEARSE_ROLLBACK:-0}" == 1 ]]; then
  echo 'Rehearsal: simulating a failed post-deploy check' >&2
  fail_after_change
fi

echo "Deployment succeeded: ${image}:${target_tag} (${running_image_id})"
