#!/usr/bin/env bash
# Backs up PostgreSQL and Redis from the VPS staging stack into backups/<timestamp>/.
# Usage: ./scripts/backup-vps.sh
# Keep backups for a different number of days: KEEP_DAYS=30 ./scripts/backup-vps.sh
set -euo pipefail

cd "$(dirname "$0")/.."

compose=(docker compose --env-file .env.vps -f docker-compose.vps.yaml)
dir="backups/$(date -u +%Y%m%d-%H%M%S)"
keep_days="${KEEP_DAYS:-14}"

mkdir -p "$dir"

# Credentials come from the container's own environment, so they stay out of this script.
# -Fc is PostgreSQL's compressed custom format, restored with pg_restore.
echo "Backing up PostgreSQL..."
"${compose[@]}" exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$dir/postgres.dump"

# BGREWRITEAOF compacts the append-only file first, so the archive is small and current.
echo "Backing up Redis..."
"${compose[@]}" exec -T redis redis-cli BGREWRITEAOF >/dev/null
sleep 3
"${compose[@]}" exec -T redis tar -cf - -C /data . > "$dir/redis-data.tar"

echo "Backup written to $dir"
du -h "$dir"/*

# Retention: drop backup folders older than keep_days.
find backups -mindepth 1 -maxdepth 1 -type d -mtime "+$keep_days" -exec rm -rf {} +
