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

Read-only host diagnostics, from the `deploy` shell:

```bash
uptime                              # load and uptime
free -h                             # memory
df -h /                             # free space on root filesystem
systemctl is-active docker nginx    # host services
sudo journalctl -u docker -n 30 --no-pager  # recent Docker service events
sudo ss -lntp                        # listening TCP ports and owning processes
getent hosts gameserver-staging.bobak.me.uk # DNS resolution
curl -fsS http://127.0.0.1:8080/ping         # API without nginx
curl -fsS https://gameserver-staging.bobak.me.uk/ping # public HTTPS route
```

Both `curl` calls should return `pong`. This only checks the HTTP process, not Redis or
PostgreSQL. Port `8080` should bind to loopback; nginx listens on `80` and `443`.

Deploy a specific build from branch `VPS`, or restore the locally saved previous image:

```bash
./scripts/deploy-vps.sh sha-3842cd0
./scripts/deploy-vps.sh rollback-last-good
```

Images are built by GitHub Actions and pulled from GHCR; nothing is compiled on the VPS.
For the manual GitHub Actions deployment and SSH setup, see
[RUNBOOK_DEPLOY_STAGING.md](RUNBOOK_DEPLOY_STAGING.md).

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

## Alerting

Grafana evaluates the rules and sends notifications to a Telegram bot. The bot token lives in
Grafana's own database, not in git.

| Rule | Query | Fires when | Pending |
| --- | --- | --- | --- |
| API is down | `up{job="gameserver-api"}` | below 1 | 2m |
| PostgreSQL is down | `pg_up` | below 1 | 2m |
| Disk almost full | `100 * (1 - node_filesystem_avail_bytes{mountpoint="/"} / node_filesystem_size_bytes{mountpoint="/"})` | above 85 | 5m |
| Memory pressure | `100 * (1 - node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes)` | above 90 | 10m |
| High 5xx rate | `100 * (sum(rate(http_requests_received_total{code=~"5.."}[5m])) or vector(0)) / clamp_min(sum(rate(http_requests_received_total[5m])), 0.001)` | above 5 | 5m |

The comparison belongs in the Threshold block, not in the PromQL. With `== 0` or `> 85` inside
the query, a healthy system returns an empty result and Grafana raises `DatasourceNoData`
instead of staying quiet. Each rule also sets *no data* to Normal and *execution error* to
Alerting, so a broken Prometheus is still reported.

The pending period is what keeps a deploy quiet: `deploy-vps.sh` leaves the API down for a few
seconds, well inside the 2 minutes.

Planned downtime goes through **Alerting → Silences** rather than disabling a rule, which is
easy to forget to undo.

Threshold rules are tested by lowering the threshold below the current value, waiting for the
notification, and putting it back — never by actually filling the disk.

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
- A green Prometheus target only means the exporter answered. It does not mean the exporter
  reached its data source — check `pg_up` and `redis_up` for that. A postgres-exporter that
  could not authenticate sat green and unnoticed for days.
- `deploy` is in the `docker` group, which is equivalent to root on this host. Treat SSH access
  to it as full machine access.
- Backup, offsite restore and manual image-tag rollback have been rehearsed; the automated
  staging deployment and its failure rollback still require a live run.
- Do not point the mobile client or closed-testing DNS at this VPS before that.
