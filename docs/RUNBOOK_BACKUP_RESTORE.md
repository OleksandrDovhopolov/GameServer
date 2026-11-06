# Runbook: Backup and Restore (VPS staging)

Covers the Redis and PostgreSQL data of the `gameserver-vps` stack on `gameserver-staging-01`.
Unless a section says Windows workstation, commands run on the VPS from `~/apps/GameServer`
as the `deploy` user.

## Where backups live

`~/apps/GameServer/backups/<UTC timestamp>/`, one folder per run:

| File | Contents | Restored with |
| --- | --- | --- |
| `postgres.dump` | Logical dump of the whole database, custom compressed format | `pg_restore` |
| `redis-data.tar` | The Redis `/data` directory: append-only files plus `dump.rdb` | Replacing the volume contents |

`backups/` is in `.gitignore` — backups hold player data and must never be committed.
Backups older than 14 days are deleted on the next run (`KEEP_DAYS` overrides this).

The nightly job also uploads each new backup to a separate S3 bucket. Until the S3 setup
below is completed, the job will create a local copy but exit with an error; this makes a
missing offsite copy visible in `~/backup.log`.

### Set up offsite storage (once)

The backup Terraform stack is **separate** from the disposable AWS learning stack in
`infra/`. Its state is `s3://gameserver-tfstate-bobak/backup/terraform.tfstate`, not
`learn/terraform.tfstate`. The backup bucket is private, uses SSE-S3 encryption and
versioning, and expires current objects after 30 days and noncurrent versions after another
30 days. The uploader can write under `staging/` but cannot read, list or delete backups.

On the Windows workstation, with the existing AWS admin profile, run from the repository:

```powershell
aws sts get-caller-identity
terraform -chdir=infra/backup init
terraform -chdir=infra/backup plan
terraform -chdir=infra/backup apply
terraform -chdir=infra/backup output
```

In order: check the account, initialize this stack, review its resources, create them, and
show the bucket and IAM user names. Do **not** run `terraform destroy` in `infra/backup`;
the bucket also has `prevent_destroy` as a safeguard. The learning stack in `infra/` can
still be destroyed independently.

In AWS Console open **IAM → Users → gameserver-staging-backup-uploader → Security
credentials → Create access key**. Choose the option for an application outside AWS.
Copy the access key ID and secret key directly to the VPS setup prompts; never commit,
paste in chat, or place them in a Terraform variable/state file. The IAM user needs no
Console login. Delete the key in IAM when rotating or retiring this setup.

On the VPS, install AWS CLI v2 using the [official Linux instructions](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html),
then, as `deploy`:

```bash
aws configure --profile gameserver-backup
chmod 700 ~/.aws
chmod 600 ~/.aws/credentials
cp .env.backup.example .env.backup
nano .env.backup
chmod 600 .env.backup
```

`aws configure` asks for the dedicated access key, secret key, region `eu-north-1` and
output format `json`. The example `.env.backup` names the bucket in AWS account
`361960318599`; verify this is the account shown by `aws sts get-caller-identity`.
The `.env.backup` file is gitignored. These steps require the new repository files to have
been delivered to `~/apps/GameServer` first.

Run one manual backup from `~/apps/GameServer`:

```bash
./scripts/backup-vps.sh
```

It creates both local files, checks they are non-empty, writes `SHA256SUMS`, uploads the
two data files, then uploads `SHA256SUMS` last. The final `Offsite backup complete:` line
contains the S3 path. If upload fails, the local files remain and the command exits with
an error. The existing `deploy` cron entry runs the same script at 03:30 UTC; check
`~/backup.log` the next day.

From the Windows workstation, use the admin AWS profile to list and download the new copy:

```powershell
$bucket = terraform -chdir=infra/backup output -raw bucket_name
aws s3 ls "s3://$bucket/staging/" --recursive
aws s3 cp "s3://$bucket/staging/<UTC timestamp>/" backups/offsite-check/ --recursive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-offsite-backup.ps1 -BackupDir .\backups\offsite-check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\rehearse-offsite-restore.ps1 -BackupDir .\backups\offsite-check
```

Check that the listing has `postgres.dump`, `redis-data.tar` and `SHA256SUMS` for the
same timestamp. Replace `<UTC timestamp>` with the folder printed by the backup script.
The first script compares both files with the SHA-256 hashes in `SHA256SUMS`. The second
starts disposable PostgreSQL and Redis containers with no published ports or connection
to the running stacks, restores the downloaded data, checks table/key counts, and removes
those containers and their temporary Redis volume. It does not call `restore-vps.sh`:
that script targets the live VPS stack and replaces both databases. Record the result in
the verification log below. A successful upload alone does not complete OPS-3.

## Take a backup

```bash
./scripts/backup-vps.sh
```

Runs against the live stack with no downtime. It prints the target folder and the size of
each file. A `postgres.dump` of a few kilobytes is normal for an empty staging database;
zero bytes is not and means the dump failed.

## Restore

Destructive: every current key and table is replaced. The script asks for confirmation,
stops the API during the swap, and finishes with the smoke test.

```bash
./scripts/restore-vps.sh backups/<timestamp>
```

What it does, in order:

1. Stops the `api` container, so nothing writes while the databases are swapped.
2. Drops and recreates the PostgreSQL database, then loads `postgres.dump`.
3. Stops `redis`, replaces its `/data` directory from `redis-data.tar`, starts it again.
   Redis has to be down for this: on shutdown it would otherwise write memory over the
   restored files.
4. Starts the `api` container.
5. Runs `./scripts/smoke-vps.sh`.

Expected tail of the output: three `[OK]` lines, `Smoke test passed.`, `Restore finished`.

## Verify after a restore

The smoke test only proves the services answer. Also confirm the data came back:

```bash
docker compose --env-file .env.vps -f docker-compose.vps.yaml exec -T postgres \
  psql -U gameserver -d gameserver_staging -c '\dt'
```

```bash
docker compose --env-file .env.vps -f docker-compose.vps.yaml exec -T redis redis-cli DBSIZE
```

Tables from the SQL schemas should be listed, and `DBSIZE` should match what the backup held.

## If the restore fails

The stack is left with the API stopped. Nothing is lost that was not already replaced, but
the database may be half-restored, so do not start the API against it.

1. Read the error. A failed `pg_restore` usually names the missing role or table.
2. Retry with an earlier backup folder.
3. If PostgreSQL itself will not start, check `docker compose logs postgres`.
4. Bring the API back only after the smoke test passes:
   `docker compose --env-file .env.vps -f docker-compose.vps.yaml start api`

## Verification log

A backup is only proven by a restore. Add a line here after every rehearsal.

| Date | Backup | Result |
| --- | --- | --- |
| 2026-10-13 | `20261013-092854` | Dropped all `players` rows and changed a Redis resource value, then restored. All 3 players came back with their original ids; `save:restore-test-player:resources` went back from `{"Gold":1250}` to `{"Gold":250}`. Smoke test passed. |
| 2026-10-16 | — | Scheduled run confirmed: three consecutive cron backups (14, 15, 16 Oct at 03:30:01) in `~/backup.log`, each with both files. |
| 2026-11-03 | `sha-44a79ef` -> `sha-f8e956b` -> `sha-44a79ef` | Rollback rehearsal passed. API was deployed from the previous image tag, `docker ps` and smoke test confirmed it, public `/ping` returned `pong`, then the current image was restored. Redis and PostgreSQL were not changed. |
| 2026-11-05 | `20261105-161229` | First manual offsite upload: `backup-vps.sh` reported completion, and the S3 Console showed `postgres.dump`, `redis-data.tar` and `SHA256SUMS`. Download, checksum verification, isolated restore and scheduled cron upload remain to be tested. |
| 2026-11-06 | `20261106-033001` | S3 contained all three objects from the 03:30 UTC run. Downloaded them to a gitignored local folder; both SHA-256 checks passed. In isolated Docker containers, PostgreSQL restored 5 public tables and Redis restored 1 key. Temporary containers and volume were removed. Live staging was untouched. The VPS cron log itself was not read in this rehearsal. |
| 2026-11-06 | `20261106-033001` | The user subsequently checked `~/backup.log` on the VPS: this scheduled run wrote both local files and logged `Offsite backup complete` for the same S3 prefix. |

## Related

- Deploy and rollback by image tag: `./scripts/deploy-vps.sh [sha-<commit>]`
- Stack layout and TLS: [DEVOPS_VPS.md](DEVOPS_VPS.md)
