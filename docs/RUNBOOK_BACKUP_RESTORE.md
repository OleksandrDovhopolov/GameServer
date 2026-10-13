# Runbook: Backup and Restore (VPS staging)

Covers the Redis and PostgreSQL data of the `gameserver-vps` stack on `gameserver-staging-01`.
All commands run on the VPS, from `~/apps/GameServer`, as the `deploy` user.

## Where backups live

`~/apps/GameServer/backups/<UTC timestamp>/`, one folder per run:

| File | Contents | Restored with |
| --- | --- | --- |
| `postgres.dump` | Logical dump of the whole database, custom compressed format | `pg_restore` |
| `redis-data.tar` | The Redis `/data` directory: append-only files plus `dump.rdb` | Replacing the volume contents |

`backups/` is in `.gitignore` — backups hold player data and must never be committed.
Backups older than 14 days are deleted on the next run (`KEEP_DAYS` overrides this).

They sit on the same disk as the containers, which protects against a bad migration or a
dropped table, but not against losing the server. Copy them off the VPS periodically:

```bash
scp -r staging:apps/GameServer/backups/<timestamp> ./backups/
```

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

## Related

- Deploy and rollback by image tag: `./scripts/deploy-vps.sh [sha-<commit>]`
- Stack layout and TLS: [DEVOPS_VPS.md](DEVOPS_VPS.md)
