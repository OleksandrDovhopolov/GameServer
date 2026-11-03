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

Run the smoke test:

```powershell
.\scripts\smoke-test.ps1
```

Expected result:

```text
[OK] API /ping returned pong
[OK] Redis returned PONG
[OK] Postgres returned 1
Smoke test passed.
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

## Local Kubernetes API Exercise

The separate `kind-gameserver-local` cluster has one Docker-backed node and a
`gameserver-local` namespace. Manifests and repeatable commands are in
[k8s/api-local/README.md](../k8s/api-local/README.md).

Only the API runs in this cluster. Its Deployment uses the GHCR image
`sha-44a79ef`, a ConfigMap for non-secret settings, and a cluster-created Secret
for throwaway local credentials. The `gameserver-api` Service is `ClusterIP`
on port 8080, so access from Windows uses a temporary port-forward:

```powershell
kubectl -n gameserver-local port-forward service/gameserver-api 18080:8080
```

Then `curl.exe http://localhost:18080/ping` returns `pong`. `18080` is the
Windows-side port; `8080` is the Service/API port inside Kubernetes.

Redis and PostgreSQL are not deployed there. Their connection strings in the
ConfigMap are placeholders, and `/ping` checks HTTP only. Do not use this
exercise to test game endpoints or database readiness. Docker Compose local
staging remains a separate stack with real Redis and PostgreSQL.

The Deployment was scaled to two Pods for a rollout exercise; image
`sha-f8e956b` was rolled out, then `kubectl rollout undo` restored `sha-44a79ef`.
The manifest still specifies `replicas: 1`: reapplying it restores one Pod.
