#!/usr/bin/env bash
# Restores PostgreSQL and Redis from a backup folder created by backup-vps.sh.
# DESTRUCTIVE: every current key and table is replaced by the backup.
# Usage: ./scripts/restore-vps.sh backups/20261013-101500
set -euo pipefail

cd "$(dirname "$0")/.."

dir="${1:?Usage: ./scripts/restore-vps.sh <backup-dir>}"
[ -f "$dir/postgres.dump" ] || { echo "Missing $dir/postgres.dump"; exit 1; }
[ -f "$dir/redis-data.tar" ] || { echo "Missing $dir/redis-data.tar"; exit 1; }

compose=(docker compose --env-file .env.vps -f docker-compose.vps.yaml)

echo "This REPLACES all PostgreSQL and Redis data with the backup in $dir."
read -r -p "Type 'restore' to continue: " answer
[ "$answer" = "restore" ] || { echo "Aborted."; exit 1; }

# Stop the API first so nothing writes while the databases are swapped.
echo "Stopping API..."
"${compose[@]}" stop api

echo "Restoring PostgreSQL..."
"${compose[@]}" exec -T postgres sh -c \
  'dropdb -U "$POSTGRES_USER" --force --if-exists "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
"${compose[@]}" exec -T postgres \
  sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$dir/postgres.dump"

# Redis must be down while its data directory is replaced, otherwise it would
# overwrite the restored files from memory on shutdown.
echo "Restoring Redis..."
"${compose[@]}" stop redis
docker run --rm -i --volumes-from "$("${compose[@]}" ps -aq redis)" alpine \
  sh -c 'rm -rf /data/appendonlydir /data/appendonly.aof /data/dump.rdb && tar -xf - -C /data' \
  < "$dir/redis-data.tar"
"${compose[@]}" start redis

echo "Starting API..."
"${compose[@]}" start api

./scripts/smoke-vps.sh
echo "Restore finished from $dir"
