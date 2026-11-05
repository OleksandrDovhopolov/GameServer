#!/usr/bin/env bash
# Upload one complete VPS backup to S3. SHA256SUMS is uploaded last as the completion marker.
# Usage: ./scripts/upload-backup-s3.sh backups/20261105-120000
set -euo pipefail

cd "$(dirname "$0")/.."

dir="${1:?Usage: ./scripts/upload-backup-s3.sh backups/<UTC timestamp>}"
config="${BACKUP_S3_CONFIG:-.env.backup}"
[[ -f "$config" ]] || { echo "Missing $config; offsite backup is not configured" >&2; exit 1; }

# This file contains only non-secret settings. AWS credentials live in ~/.aws/credentials.
# shellcheck disable=SC1090
source "$config"
: "${BACKUP_S3_BUCKET:?Set BACKUP_S3_BUCKET in $config}"
: "${BACKUP_S3_REGION:?Set BACKUP_S3_REGION in $config}"
: "${BACKUP_AWS_PROFILE:?Set BACKUP_AWS_PROFILE in $config}"
prefix="${BACKUP_S3_PREFIX:-staging}"
[[ "$prefix" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo "Invalid BACKUP_S3_PREFIX" >&2; exit 1; }

dir="$(cd "$dir" && pwd -P)"
stamp="$(basename "$dir")"
[[ "$stamp" =~ ^[0-9]{8}-[0-9]{6}$ ]] || { echo "Expected a UTC timestamp backup directory" >&2; exit 1; }
[[ -s "$dir/postgres.dump" && -s "$dir/redis-data.tar" ]] || {
  echo "Both backup files must exist and be non-empty" >&2
  exit 1
}

(cd "$dir" && sha256sum postgres.dump redis-data.tar > SHA256SUMS)
destination="s3://$BACKUP_S3_BUCKET/$prefix/$stamp"

# Upload the manifest last. A backup without SHA256SUMS is incomplete.
for name in postgres.dump redis-data.tar SHA256SUMS; do
  aws --profile "$BACKUP_AWS_PROFILE" --region "$BACKUP_S3_REGION" \
    s3 cp "$dir/$name" "$destination/$name" --no-progress --only-show-errors
done

echo "Offsite backup complete: $destination/"
