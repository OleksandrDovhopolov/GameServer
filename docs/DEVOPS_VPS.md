# VPS Staging

Staging server `gameserver-staging-01` (Hetzner CX33, Ubuntu). Separate from the Railway
closed-testing backend, with its own secrets and its own data.

Project root on the server: `~/apps/GameServer`, user `deploy`, branch `VPS`.

- Public API: <https://gameserver-staging.bobak.me.uk>
- Grafana: <https://grafana.bobak.me.uk>

What has been built, with dates, is in [PROGRESS.md](PROGRESS.md).

## Current layout

```text
internet
   |
   +-- :443 nginx (host) -- gameserver-staging.bobak.me.uk --> 127.0.0.1:8080 --> api
   |                     -- grafana.bobak.me.uk            --> 127.0.0.1:3000 --> grafana
   |
   firewall: 22, 80, 443 only

docker network gameserver-vps_default
   api -------> redis:6379
   api -------> postgres:5432
   prometheus -> node-exporter:9100, redis-exporter:9121, postgres-exporter:9187
   grafana ---> prometheus:9090
```

| Layer | Address | Exposure |
| --- | --- | --- |
| nginx | `:80`, `:443` | Public. Terminates TLS, proxies by hostname |
| API | `127.0.0.1:8080` → `api:8080` | Loopback only, reached through nginx |
| Redis | `redis:6379` | Docker network only, no host port |
| PostgreSQL | `postgres:5432` | Docker network only, no host port |
| Grafana | `127.0.0.1:3000` | Loopback only, reached through nginx |
| Prometheus | `127.0.0.1:9090` | Loopback only, reached through an SSH tunnel |
| Exporters | `:9100`, `:9121`, `:9187` | Docker network only, no host port |

The API container does not own TLS. Keeping HTTPS at nginx makes certificate renewal,
redirects, access logs and host-based routing easier to manage.

## Two compose projects

| Project | File | Env file | Contents |
| --- | --- | --- | --- |
| `gameserver-vps` | `docker-compose.vps.yaml` | `.env.vps` | api, redis, postgres |
| `gameserver-monitoring` | `docker-compose.monitoring.yaml` | `.env.monitoring` | prometheus, grafana, 3 exporters |

They are deliberately separate so that a deploy of the API never treats the monitoring
containers as orphans. The monitoring project joins the app network as `external`, which is how
the exporters reach `redis` and `postgres` by service name.

Shell aliases on the server:

```bash
dc   # docker compose --env-file ~/apps/GameServer/.env.vps        -f ~/apps/GameServer/docker-compose.vps.yaml
dcm  # docker compose --env-file ~/apps/GameServer/.env.monitoring -f ~/apps/GameServer/docker-compose.monitoring.yaml
```

## Environment files

```bash
cp .env.vps.example .env.vps && chmod 600 .env.vps
cp .env.monitoring.example .env.monitoring && chmod 600 .env.monitoring
```

Run those once. Copying again overwrites the real file with the template.

`POSTGRES_PASSWORD` appears in both files — the API and the Prometheus exporter use the same
database account, so a rotation has to update both. See [RUNBOOK_SECURITY.md](RUNBOOK_SECURITY.md).

Never reuse Railway production secrets, closed-testing data, or local staging passwords here.

## Everyday commands

Deploy the latest build of branch `VPS`, then roll back to a specific one:

```bash
./scripts/deploy-vps.sh
./scripts/deploy-vps.sh sha-3842cd0
```

Images are built by GitHub Actions and pulled from GHCR; nothing is compiled on the VPS.

Smoke test on its own:

```bash
./scripts/smoke-vps.sh
API_URL=http://localhost:8080 ./scripts/smoke-vps.sh   # bypass nginx and TLS
```

Backup and restore — see [RUNBOOK_BACKUP_RESTORE.md](RUNBOOK_BACKUP_RESTORE.md):

```bash
./scripts/backup-vps.sh
./scripts/restore-vps.sh backups/<timestamp>
```

Monitoring stack:

```bash
dcm up -d
dcm ps
```

Prometheus has no public address. Reach it from a workstation with a tunnel, then open
`http://localhost:9090`:

```bash
ssh -L 9090:127.0.0.1:9090 deploy@<vps-ip>
```

## TLS

Certificates come from Let's Encrypt through certbot, which writes the TLS block into the nginx
site config itself. Renewal runs from a systemd timer. Verify it without spending a real
certificate:

```bash
sudo certbot renew --dry-run
systemctl list-timers 'certbot*'
```

Adding another subdomain: DNS A record, nginx site in `/etc/nginx/sites-available`, symlink into
`sites-enabled`, `sudo nginx -t`, reload, then `sudo certbot --nginx -d <host>`.

## Safety notes

- Staging secrets only. Nothing with credentials goes into git — `.env.*` is ignored, `.env.*.example`
  is the template that is committed.
- Redis and PostgreSQL stay closed to the internet: no published host ports.
- Firewall opens 22, 80 and 443 only. Admin surfaces are reached through nginx or an SSH tunnel.
- Docker logs are capped via `/etc/docker/daemon.json` so containers cannot fill the disk.
- Run the smoke test after every deploy and after any infrastructure change.
- `deploy` is in the `docker` group, which is equivalent to root on this host. Treat SSH access
  to it as full machine access.
- Not production-ready until backup, restore and rollback have each been rehearsed. Backup and
  restore were verified on 2026-10-13; rollback by image tag is still unrehearsed.
- Do not point the mobile client or closed-testing DNS at this VPS before that.
