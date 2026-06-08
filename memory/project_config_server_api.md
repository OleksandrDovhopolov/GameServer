---
name: project-config-server-api
description: Config Server API feature — implemented endpoints, storage layer, key design decisions
metadata:
  type: project
---

Config Server API реализован согласно спеке `docs/CONFIG_SERVER_API.md`.

**Why:** Клиент (Unity) уже готов и ходит по этим маршрутам. Нужно было реализовать backend.

**How to apply:** При изменениях в Config API ориентироваться на эти решения.

## Реализованные файлы

- `Data/configs_schema.sql` — SQL для создания таблицы вручную
- `Models/Domain/Config/ConfigVersionRecord.cs`
- `Models/Dto/Configs/` — 4 DTO (ManifestEntry, AdminConfig, HistoryEntry, PublishRequest)
- `Repositories/Interfaces/IConfigRepository.cs`
- `Repositories/PostgresConfigRepository.cs` — первое реальное использование Postgres+Dapper в проекте
- `Services/Interfaces/IConfigService.cs`
- `Services/ConfigService.cs`
- `Controllers/ConfigsController.cs` — public routes
- `Controllers/AdminConfigsController.cs` — admin routes
- `Tests/ConfigServiceTests.cs` — 17 тестов, все проходят

## Маршруты

- `GET /api/v1/configs/manifest?environment=` (public)
- `GET /api/v1/configs/{name}?environment=` (public, If-None-Match → 304)
- `GET/PUT /api/v1/admin/configs/{name}?environment=` (Basic Auth)
- `GET /api/v1/admin/configs/{name}/history?environment=`
- `POST /api/v1/admin/configs/{name}/rollback?environment=&to=`
- `POST /api/v1/admin/configs/{name}/promote?from=&to=`

## Ключевые решения

- **Хранилище**: PostgreSQL + Dapper (первое использование), таблица `configs` с историей версий
- **NpgsqlDataSource**: регистрируется как singleton через `NpgsqlDataSource.Create()` (без AddNpgsqlDataSource — нет пакета DI)
- **ETag**: SHA-256 от минифицированного JSON, первые 12 символов hex
- **Optimistic concurrency**: `PublishAsync` проверяет `If-Match` vs текущего etag → `null` = 412
- **Promote**: отдельный `POST /promote?from=dev&to=prod` (не PUT), чтобы гарантировать байт-в-байт копию
- **Environment**: клиент передаёт явно через query param `?environment=`
- **Admin auth**: переиспользован `AdminBasicAuthFilter` (Basic Auth)
- **Swagger**: обновлён `AdminBasicAuthOperationFilter` для поддержки `api/v1/admin/` маршрутов
- **`configs_schema.sql`**: запускается вручную (нет migration runner)
