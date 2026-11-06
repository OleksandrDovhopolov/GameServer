# Контролируемый деплой на staging VPS

Схема: тесты и сборка в GitHub Actions → образ `ghcr.io/oleksandrdovhopolov/gameserver-api:sha-...` → ручной запуск workflow → SSH под `deploy` → `git pull --ff-only` → `deploy-vps.sh` → Bash smoke test. Railway и его данные не участвуют.

## Что хранится где

- На VPS: `.env.vps`, контейнеры и данные, доступ к GHCR (если пакет приватный).
- В GitHub Actions: отдельный SSH-ключ для staging и проверенный host key VPS.
- В Git: workflow, Compose и скрипты. Секреты и локальный Ansible inventory в Git не добавлять.

## Подготовка вручную

1. Закоммитить и отправить изменения в ветку `VPS`. Убедиться, что тесты и сборка образа прошли. Для кнопки **Run workflow** файл `.github/workflows/deploy-staging.yml` также должен попасть в ветку репозитория по умолчанию (проверьте её в GitHub → Settings → Branches; возможно, это `main`). Можно перенести туда только файл workflow отдельным PR, не сливая остальные изменения ветки. Один лишь push в `VPS` недостаточен. При запуске выбрать ветку `VPS`.
2. На своём ПК создать отдельный SSH-ключ Ed25519 для GitHub Actions:

   ```powershell
   ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\gameserver_staging_ci" -C "github-actions-staging"
   Get-Content "$env:USERPROFILE\.ssh\gameserver_staging_ci.pub"
   ```

   У этого ключа не должно быть интерактивной парольной фразы, иначе runner не сможет подключиться. Через уже работающий личный SSH-вход на VPS выполнить `mkdir -p ~/.ssh && chmod 700 ~/.ssh`, открыть `nano ~/.ssh/authorized_keys`, добавить публичную строку отдельной строкой, затем выполнить `chmod 600 ~/.ssh/authorized_keys`. Приватный ключ (`gameserver_staging_ci`, без `.pub`) сохранить только в GitHub Actions secret `STAGING_SSH_KEY`. Не использовать личный SSH-ключ и не коммитить ключ в репозиторий.
3. На ПК получить публичный host key VPS и его отпечаток:

   ```powershell
   $vpsIp = '2.28.134.69'  # сверьте актуальный Public IP в Hetzner
   ssh-keyscan -t ed25519 $vpsIp | Set-Content -Encoding ascii "$env:TEMP\gameserver_known_hosts"
   ssh-keygen -lf "$env:TEMP\gameserver_known_hosts"
   Get-Content "$env:TEMP\gameserver_known_hosts"
   ```

   Сверить отпечаток через уже доверенную SSH-сессию с `sudo ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`. Проверенную строку из файла сохранить в GitHub Actions secret `STAGING_KNOWN_HOSTS`. Новые ключи хоста вслепую не принимать.
4. В GitHub: **Settings → Secrets and variables → Actions**. В *Repository secrets* добавить два значения выше; в *Variables* добавить `STAGING_HOST` с публичным IP или DNS VPS. Имя в проверенной строке `known_hosts` должно совпадать со значением `STAGING_HOST`. Workflow всегда подключается пользователем `deploy`.
5. На VPS проверить `git pull --ff-only origin VPS`, `./scripts/smoke-vps.sh` и доступность нужного образа GHCR через `docker pull ghcr.io/oleksandrdovhopolov/gameserver-api:sha-<commit>`. Если репозиторий или пакет приватный, VPS должен иметь отдельный доступ только для чтения; временный `GITHUB_TOKEN` из build workflow для этого не подходит.

Ключ GitHub Actions открывает аккаунт `deploy`. Поскольку `deploy` состоит в группе `docker`, этот доступ практически равен правам администратора staging VPS. Ограничить доступ к репозиторию и его секретам; при утечке удалить строку публичного ключа из `authorized_keys` и заменить secret.

## Запуск и проверка

В GitHub открыть **Actions → Deploy staging VPS → Run workflow**, выбрать ветку `VPS` и указать уже опубликованный тег вида `sha-44a79ef`. Workflow принимает только коммит из истории ветки `VPS`. Параллельные запуски сериализуются; сам скрипт на VPS использует локальный lock для ручного и CI-деплоя.

На VPS `deploy-vps.sh` сохраняет ID прежнего образа под локальным тегом `rollback-last-good`, скачивает выбранный образ и пересоздаёт только API. Он ждёт локальный `/ping`, затем Bash smoke test проверяет публичный `/ping`, Redis и PostgreSQL. После успеха сверяет ID работающего контейнера с ID выбранного образа. Логи workflow должны показать тег и `Deployment succeeded`.

Если `pull` не удался, работающий API не меняется. Если обновление или проверка после него не удались, скрипт возвращает прежний образ, повторяет smoke test и завершает workflow с ошибкой. При сообщении `ROLLBACK FAILED` проверить контейнер и зависимости вручную до следующего деплоя. Старые образы автоматически не удаляются; после проверки свободного места удалять ненужные теги отдельно.

Откат после уже успешного деплоя на VPS:

```bash
cd ~/apps/GameServer
./scripts/deploy-vps.sh rollback-last-good
```

Для ручного возврата к конкретной опубликованной версии используйте `./scripts/deploy-vps.sh sha-<commit>`. Тег `staging` меняется после каждой новой сборки и не годится для указания точной версии. Тег `rollback-last-good` существует только на этом VPS.

## Репетиция отката

На staging, после успешного деплоя одной версии, запустить **другую существующую** версию с учебной ошибкой после проверки:

```bash
DEPLOY_REHEARSE_ROLLBACK=1 ./scripts/deploy-vps.sh sha-<другой-существующий-коммит>
```

Ожидается ненулевой код завершения и сообщение `Rollback succeeded`. Убедиться, что публичный `/ping`, Redis и PostgreSQL снова доступны и что ID API совпадает с сохранённой предыдущей версией. Эта репетиция на короткое время пересоздаёт API. Если всё успешно, записать использованные теги и результат в `PROGRESS.md`.

## Ограничения проверки

- `/ping` подтверждает ответ API, а отдельные Redis/PostgreSQL-команды подтверждают доступность зависимостей. Игровые операции этим smoke test не покрываются.
- Деплой старого образа использует текущую конфигурацию Compose из ветки `VPS`; совместимость старых версий с новой схемой БД надо проверять отдельно.
- Прерывание runner или потеря SSH-соединения не гарантирует выполнение автоматического отката. В таком случае зайти на VPS и проверить состояние вручную.
