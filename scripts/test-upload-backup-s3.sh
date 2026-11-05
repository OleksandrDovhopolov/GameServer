#!/usr/bin/env bash
# Local test: fake AWS CLI checks upload order and failure handling; no network needed.
set -euo pipefail
cd "$(dirname "$0")/.."

tmp="$(mktemp -d)"
cleanup() {
  rm -f "$tmp/bin/aws" "$tmp/20261105-120000/postgres.dump" \
    "$tmp/20261105-120000/redis-data.tar" "$tmp/20261105-120000/SHA256SUMS" \
    "$tmp/config" "$tmp/aws.log"
  rmdir "$tmp/bin" "$tmp/20261105-120000" "$tmp"
}
trap cleanup EXIT
mkdir -p "$tmp/bin" "$tmp/20261105-120000"
printf 'postgres test\n' > "$tmp/20261105-120000/postgres.dump"
printf 'redis test\n' > "$tmp/20261105-120000/redis-data.tar"
cat > "$tmp/config" <<'EOF'
BACKUP_S3_BUCKET=example-backups
BACKUP_S3_REGION=eu-north-1
BACKUP_S3_PREFIX=staging
BACKUP_AWS_PROFILE=gameserver-backup
EOF
cat > "$tmp/bin/aws" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$MOCK_AWS_LOG"
if [[ "${MOCK_FAIL_ON:-}" == "$*" ]]; then exit 42; fi
EOF
chmod +x "$tmp/bin/aws"

export BACKUP_S3_CONFIG="$tmp/config" MOCK_AWS_LOG="$tmp/aws.log"
export PATH="$tmp/bin:$PATH"
bash ./scripts/upload-backup-s3.sh "$tmp/20261105-120000"
[[ "$(wc -l < "$tmp/aws.log")" -eq 3 ]]
tail -n 1 "$tmp/aws.log" | grep -q 'SHA256SUMS'
(cd "$tmp/20261105-120000" && sha256sum -c SHA256SUMS)

: > "$tmp/aws.log"
export MOCK_FAIL_ON="--profile gameserver-backup --region eu-north-1 s3 cp $tmp/20261105-120000/redis-data.tar s3://example-backups/staging/20261105-120000/redis-data.tar --no-progress --only-show-errors"
if bash ./scripts/upload-backup-s3.sh "$tmp/20261105-120000"; then
  echo 'Expected upload failure' >&2
  exit 1
fi
[[ "$(wc -l < "$tmp/aws.log")" -eq 2 ]]
if grep -q 'SHA256SUMS' "$tmp/aws.log"; then
  echo 'Completion marker uploaded after failure' >&2
  exit 1
fi
echo 'Upload script checks passed.'
