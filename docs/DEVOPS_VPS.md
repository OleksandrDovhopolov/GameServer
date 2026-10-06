# VPS Staging

This is the next deployment shape after local staging. It is intended for a Hetzner VPS staging server, not for the current closed-testing Railway backend.

## Compose File

Use:

```powershell
docker compose --env-file .env.vps -f docker-compose.vps.yaml up -d --build
```

The VPS compose file differs from local staging in two important ways:

- Redis and PostgreSQL are not published to host ports.
- The API is published only on `127.0.0.1:8080`, so it is reachable from the VPS itself, but not directly from the internet.

That layout prepares the server for nginx:

```text
internet -> nginx :443 -> 127.0.0.1:8080 -> api container
api container -> redis:6379
api container -> postgres:5432
```

## Environment

Copy the example file and replace every placeholder:

```powershell
Copy-Item .env.vps.example .env.vps
```

Never reuse Railway production secrets, closed-testing data, or local staging passwords on the VPS.

## Smoke Test

Before nginx is added, run the smoke test from the VPS itself:

```powershell
.\scripts\smoke-test.ps1 -ComposeFile docker-compose.vps.yaml -EnvFile .env.vps -ApiUrl http://localhost:8080
```

After nginx and TLS are configured, the API smoke test URL can become the public staging URL:

```powershell
.\scripts\smoke-test.ps1 -ComposeFile docker-compose.vps.yaml -EnvFile .env.vps -ApiUrl https://staging-api.example.com
```

## Reverse Proxy

A reverse proxy is the public front door for the backend.

For the VPS, nginx should listen on public ports `80` and `443`. It should terminate HTTPS and forward API traffic to `127.0.0.1:8080`.

The API container should not own TLS directly in this setup. Keeping HTTPS at nginx makes certificate renewal, redirects, access logs, and future routing easier to manage.

Do not point the mobile client or DNS for closed testing at the VPS until the staging URL has passed smoke tests and rollback has been rehearsed.
