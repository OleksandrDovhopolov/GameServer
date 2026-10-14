# Журнал работ по инфраструктуре

Что сделано со staging-контуром GameServer. Новые записи добавляются сверху.
Планы и незакрытые задачи — в [TODO.md](TODO.md), процедуры — в runbook'ах.

Публичный staging: <https://gameserver-staging.bobak.me.uk>
Мониторинг: <https://grafana.bobak.me.uk>

---

## 13 октября 2026 — CI/CD, бэкапы, мониторинг

### Сборка образа перенесена с VPS в GitHub Actions

- Добавлен workflow [build.yml](../.github/workflows/build.yml): на push в `main` и `VPS`
  прогоняются тесты (252 шт.), затем собирается и публикуется образ в GHCR —
  `ghcr.io/oleksandrdovhopolov/gameserver-api`.
- Задача сборки зависит от задачи тестов, поэтому образ с упавшими тестами в реестр не попадёт.
- Теги: неизменяемый `sha-<коммит>` для отката и плавающий `staging` для последней версии ветки.
- Авторизация в GHCR — через временный `GITHUB_TOKEN` с правом `packages: write`,
  долгоживущие токены нигде не хранятся.
- В `docker-compose.vps.yaml` ключ `build:` заменён на `image:` с переменной `${API_TAG:-staging}`.
  `dotnet restore/publish` больше не выполняется на VPS рядом с Redis и PostgreSQL.
- Добавлен [deploy-vps.sh](../scripts/deploy-vps.sh): `pull` → `up -d` → ожидание старта →
  smoke test → очистка повисших образов. Откат — `./scripts/deploy-vps.sh sha-<коммит>`.

### Backup и restore

- [backup-vps.sh](../scripts/backup-vps.sh) — логический дамп PostgreSQL (`pg_dump -Fc`) и архив
  каталога `/data` Redis, без остановки сервисов. Хранение 14 дней.
- [restore-vps.sh](../scripts/restore-vps.sh) — восстановление с подтверждением, остановкой API
  на время подмены данных и обязательным smoke test в конце.
- **Восстановление проверено на практике**: удалены все строки из `players` и изменено значение
  ресурсов в Redis, после восстановления вернулись три игрока с исходными идентификаторами и
  `{"Gold":250}`. Журнал — в [RUNBOOK_BACKUP_RESTORE.md](RUNBOOK_BACKUP_RESTORE.md).
- Бэкап поставлен в cron на 03:30. Запуск по расписанию ещё не подтверждён — задача OPS-1.

### Мониторинг

- Отдельный compose-проект [docker-compose.monitoring.yaml](../docker-compose.monitoring.yaml):
  Prometheus, Grafana, node-exporter, redis-exporter, postgres-exporter.
- Проект подключается к сети приложения как внешней, поэтому экспортеры обращаются к `redis`
  и `postgres` по именам сервисов. `deploy-vps.sh` при этом мониторинг не трогает.
- Порты 9090 и 3000 привязаны к `127.0.0.1`; firewall по-прежнему открывает только 22, 80, 443.
- Prometheus опрашивает цели раз в 15 секунд, хранит данные 15 дней.
- Grafana открыта по `https://grafana.bobak.me.uk` через nginx с сертификатом Let's Encrypt.
- Импортированы дашборды 1860 (Node Exporter Full), 763 (Redis), 9628 (PostgreSQL).
- Реакция графиков проверена искусственной нагрузкой на процессор и `redis-benchmark`.

### Документация

- [RUNBOOK_BACKUP_RESTORE.md](RUNBOOK_BACKUP_RESTORE.md) — где лежат бэкапы, как восстановить,
  как проверить, журнал проверок.
- [RUNBOOK_SECURITY.md](RUNBOOK_SECURITY.md) — что доступно атакующему с доступом к shell,
  чеклист защиты, порядок действий при взломе, команды ротации каждого секрета.

---

## 8–12 октября 2026 — публичный HTTPS

- Настроена ротация логов Docker через `/etc/docker/daemon.json`.
- Проверен firewall: открыты только SSH, HTTP и HTTPS.
- Установлен nginx, настроен reverse proxy на `127.0.0.1:8080`.
- Куплен домен `bobak.me.uk`, создана A-запись `gameserver-staging`.
- Установлен certbot, выпущен сертификат Let's Encrypt.
- Публичный результат: `curl https://gameserver-staging.bobak.me.uk/ping` → `pong`.

---

## 7 октября 2026 — VPS

- Создан сервер Hetzner Cloud `gameserver-staging-01` (CX23, затем CX33).
- Заведён non-root пользователь `deploy`, проверен вход по SSH.
- Установлены Docker Engine, Docker Compose plugin и Git; `deploy` добавлен в группу `docker`.
- Репозиторий склонирован на VPS, создан `.env.vps` с правами 600.
- Запущен `docker-compose.vps.yaml`: API, Redis, PostgreSQL.

---

## 6 октября 2026 — локальный staging

- Создан отдельный локальный staging-контур на Docker Compose: API, Redis, PostgreSQL.
- Проверены `/ping` → `pong`, Redis → `PONG`, PostgreSQL → таблицы из SQL-схем.
- Добавлены named volumes для сохранения данных.
- Добавлен smoke test [smoke-test.ps1](../scripts/smoke-test.ps1), проверяющий все три сервиса
  одной командой.
- Подготовлен отдельный compose-файл для будущего VPS.
