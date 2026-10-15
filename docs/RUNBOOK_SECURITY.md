# Runbook: Security and Secret Rotation (VPS staging)

Covers `gameserver-staging-01`. All commands run on the VPS as `deploy`, from `~/apps/GameServer`.

Shell aliases used below:

```bash
dc   # docker compose --env-file ~/apps/GameServer/.env.vps        -f ~/apps/GameServer/docker-compose.vps.yaml
dcm  # docker compose --env-file ~/apps/GameServer/.env.monitoring -f ~/apps/GameServer/docker-compose.monitoring.yaml
```

## What an attacker gets with shell access

File permissions (`chmod 600`) protect secrets from *other users on the box*, not from
someone who becomes `deploy`. Assume full disclosure of everything below.

| Secret | File | Impact if leaked |
| --- | --- | --- |
| `AUTH_JWT_SIGNING_KEY` | `.env.vps` | Forge a valid token for any player |
| `ADMIN_USER` / `ADMIN_PASS` | `.env.vps` | Admin API: grant resources, reset saves, edit configs |
| `POSTGRES_PASSWORD` | `.env.vps`, `.env.monitoring` | Full read/write on the database |
| `GRAFANA_ADMIN_PASSWORD` | `.env.monitoring` | Monitoring and its history |
| Player data | Redis, PostgreSQL, `backups/` | Saves, player ids, two weeks of snapshots |

**`deploy` is effectively root.** It is in the `docker` group, and `docker run -v /:/host`
hands out a root shell on the host filesystem. That is a documented property of Docker, not a
bug. Treat compromise of `deploy` as compromise of the whole machine.

The blast radius stops at staging only because staging secrets are not reused anywhere else.
Keeping that true is worth more than any other control on this page.

## Hardening checklist

- [ ] SSH password authentication disabled (`PasswordAuthentication no`)
- [ ] Root login disabled (`PermitRootLogin no`)
- [ ] SSH private key on the workstation has a passphrase
- [ ] `fail2ban` installed
- [ ] Unattended security upgrades enabled
- [x] Firewall allows only 22, 80, 443
- [x] Redis and PostgreSQL ports not published to the host
- [x] Prometheus and Grafana bound to `127.0.0.1`, Grafana reached through nginx + TLS
- [x] Admin API behind Basic Auth; `AllowRedisFlush` and `AllowPlayerSaveReset` are `false`
- [x] Secrets live only in `.env.*` files, which are gitignored
- [ ] Backups copied off the VPS

Check the SSH settings:

```bash
sudo grep -rE '^(PasswordAuthentication|PermitRootLogin)' /etc/ssh/sshd_config /etc/ssh/sshd_config.d/ 2>/dev/null
```

After editing, validate before reloading, and keep the current session open while testing a
new login in a second window:

```bash
sudo sshd -t && sudo systemctl reload ssh
```

## Detection

```bash
sudo last -n 20                      # recent logins
sudo journalctl -u ssh --since -24h  # authentication attempts
docker ps                            # containers nobody deployed
```

Grafana helps here too: an unexplained CPU climb on Node Exporter Full is a plausible first
sign of a cryptominer.

## If the VPS is suspected compromised

Order matters. Close the door first, then rotate, then rebuild.

1. **Cut access.** Close port 22 in the Hetzner firewall, or replace `~/.ssh/authorized_keys`
   with a fresh key. Use the Hetzner web console if locked out.
2. **Rotate every secret below.** All of them, not the ones that look exposed.
3. **Rebuild rather than clean.** Create a new server, restore from a backup taken *before* the
   suspected intrusion, and move DNS. A backdoor in cron, systemd or a shell profile is easy to
   miss; a rebuild is minutes of work. This is the practical argument for infrastructure as code.
4. **Record what happened** in a short postmortem: what was reached, when, how it was noticed,
   what changed afterwards.

## Secret rotation

### JWT signing key

Invalidates every issued access and refresh token: players re-authenticate, and so does any
forged token.

```bash
sed -i "s|^AUTH_JWT_SIGNING_KEY=.*|AUTH_JWT_SIGNING_KEY=$(openssl rand -base64 48)|" .env.vps
dc up -d api
```

### Admin API credentials

```bash
sed -i "s|^ADMIN_PASS=.*|ADMIN_PASS=$(openssl rand -base64 24)|" .env.vps
dc up -d api
```

### PostgreSQL password

`POSTGRES_PASSWORD` only initialises the database on first start. Changing the env var alone
does nothing — the password must be changed inside PostgreSQL, then everywhere it is used.

```bash
NEW=$(openssl rand -base64 24)
dc exec -T postgres psql -U gameserver -d gameserver_staging -c "ALTER USER gameserver WITH PASSWORD '$NEW';"
sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$NEW|" .env.vps
sed -i "s|^POSTGRES_EXPORTER_PASS=.*|POSTGRES_EXPORTER_PASS=$NEW|" .env.monitoring
dc up -d
dcm up -d postgres-exporter
unset NEW
```

Both files must be updated: the API and the Prometheus exporter use the same account.
Verify afterwards that `postgres` is still `UP` on the Prometheus targets page.

### Grafana admin password

The environment variable applies on first start only, so reset it through the CLI:

```bash
NEW=$(openssl rand -base64 24)
dcm exec grafana grafana-cli admin reset-admin-password "$NEW"
sed -i "s|^GRAFANA_ADMIN_PASSWORD=.*|GRAFANA_ADMIN_PASSWORD=$NEW|" .env.monitoring
echo "$NEW"   # store it in a password manager, then clear the scrollback
unset NEW
```

### After any rotation

```bash
./scripts/smoke-vps.sh
```

## Rotation log

| Date | Secret | Reason |
| --- | --- | --- |
| 2026-10-13 | `GRAFANA_ADMIN_PASSWORD`, initial | Monitoring stack set up |

## Related

- [RUNBOOK_BACKUP_RESTORE.md](RUNBOOK_BACKUP_RESTORE.md) — backups, restore, verification log
- [DEVOPS_VPS.md](DEVOPS_VPS.md) — stack layout, nginx, TLS
