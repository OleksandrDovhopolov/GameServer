# Local Staging

This file describes the safe local rehearsal environment before deploying GameServer to a VPS.

It does not replace the current Railway/closed-testing backend. It uses separate local ports, separate Docker volumes, and separate environment variables.

## First Run

Copy the example environment file and replace all placeholder secrets:

```powershell
Copy-Item .env.staging.example .env.staging
```

Start the stack:

```powershell
docker compose --env-file .env.staging -f docker-compose.staging.yaml up -d --build
```

Check the API:

```powershell
curl http://localhost:5080/ping
```

Expected response:

```text
pong
```

## Services

- API: `http://localhost:5080`
- Redis: `localhost:6380`
- PostgreSQL: `localhost:5433`

Redis and PostgreSQL are intentionally exposed only on non-default host ports to reduce accidental collisions with local development services.

## Data Persistence

The compose file uses named Docker volumes:

- `gameserver-staging_redis_data`
- `gameserver-staging_postgres_data`

Containers can be recreated without deleting these volumes. To remove all local staging data, run:

```powershell
docker compose --env-file .env.staging -f docker-compose.staging.yaml down -v
```

Use `down -v` only for local staging resets, never for a real server that contains important data.

## Why `--env-file` Is Required

Docker Compose uses `.env` by default for variable interpolation in the YAML file. This project can also have a separate development `.env`, so local staging commands must explicitly pass `.env.staging`.

Use this pattern for every staging compose command:

```powershell
docker compose --env-file .env.staging -f docker-compose.staging.yaml <command>
```

## Current Scope

Included:

- API
- Redis with append-only persistence
- PostgreSQL with initial schema files

Excluded for the first pass:

- Kafka
- EventsWorker
- nginx/TLS
- production Google Play billing
- monitoring

Those pieces should be added after the API/Redis/PostgreSQL staging loop is boring and repeatable.

## Closed Testing Safety

- Do not point the mobile client at this environment.
- Do not copy production Redis/PostgreSQL data into this environment.
- Do not reuse production secrets.
- Do not change Railway variables, DNS, or client URLs while working on local staging.
- Google Play Billing is disabled here by design.
