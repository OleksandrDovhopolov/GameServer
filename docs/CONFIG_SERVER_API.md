# Config Server API - backend contract

Актуальная спецификация Config Server API. Источником истины считается текущая backend-реализация.

- Base URL: `https://gameserver-production-be8b.up.railway.app`
- Public prefix: `/api/v1/configs`
- Admin prefix: `/api/admin/configs`
- Формат данных: JSON
- Хранение: versioned snapshot секции целиком

Связанные документы:
- [ADR-001-config-section-snapshot-storage.md](ADR-001-config-section-snapshot-storage.md)
- [CONFIG_CACHE_SYSTEM.md](CONFIG_CACHE_SYSTEM.md)

## 1. Основная модель

- Одна секция (`books`, `locations`, `requests`, `events`) хранится как один JSON-массив объектов.
- Одна строка в таблице `configs` = одна версия одной секции в одном `environment`.
- Любая admin-публикация создает новую версию всей секции целиком.
- `etag` считается от канонизированного minified JSON и должен совпадать между manifest и public GET текущей версии.

## 2. Public API

Public API используется игровым клиентом.

### 2.1 `GET /api/v1/configs/manifest`

Query:
- `environment` optional
- если параметр не передан, backend использует `prod`

Response `200`:

```json
[
  { "name": "books", "version": 13, "etag": "a1b2c3" },
  { "name": "locations", "version": 4, "etag": "d4e5f6" }
]
```

### 2.2 `GET /api/v1/configs/{name}`

Query:
- `environment` optional
- если параметр не передан, backend использует `prod`

Request header:

```text
If-None-Match: "a1b2c3"
```

Responses:
- `200 OK` + raw JSON array + header `ETag`
- `304 Not Modified`, если `If-None-Match` совпал с текущим `etag`
- `404 Not Found`, если секция не найдена

Пример `200`:

```text
ETag: "a1b2c3"
Content-Type: application/json
```

```json
[
  {
    "id": "book_dune",
    "title": "Dune",
    "author": "Frank Herbert",
    "genre": "sci-fi",
    "basePrice": 120,
    "rarityWeight": 0.2
  }
]
```

## 3. Admin API

Admin API используется для чтения, публикации и истории конфигов.

### 3.1 Аутентификация

- Все `/api/admin/configs/*` защищены через Basic auth.
- Bearer migration в текущем контракте нет.
- `updatedBy` берется из Basic username.

### 3.2 `GET /api/admin/configs/{name}?environment=dev`

Возвращает текущую версию секции для редактирования.

Response `200`:

```text
ETag: "a1b2c3"
```

```json
{
  "name": "books",
  "environment": "dev",
  "version": 13,
  "etag": "a1b2c3",
  "json": [
    {
      "id": "book_dune",
      "title": "Dune"
    }
  ]
}
```

Responses:
- `200 OK` + `AdminConfigDto` + header `ETag`
- `404 Not Found`
- `400 Bad Request`, если `environment` не передан

### 3.3 `PUT /api/admin/configs/{name}?environment=dev`

Публикует новую версию секции.

Request header:

```text
If-Match: "a1b2c3"
Content-Type: application/json
```

Request body:

```json
{
  "json": [
    {
      "id": "book_dune",
      "title": "Dune"
    }
  ],
  "comment": "balance pass"
}
```

Responses:
- `200 OK` - создана новая версия, тело ответа имеет тот же формат, что и `GET /api/admin/configs/{name}`
- `400 Bad Request` - отсутствует `If-Match` или payload не проходит минимальную валидацию
- `412 Precondition Failed` - `If-Match` устарел, на сервере уже есть более новая версия

Примечания:
- Для первой публикации новой секции backend ожидает любой непустой `If-Match`; на клиенте используется строка `"bootstrap"` как соглашение.
- `"bootstrap"` не является серверной константой и не требуется для всех последующих запросов.
- Publish работает по optimistic concurrency: сначала нужно прочитать текущую версию, затем отправить полный snapshot секции с актуальным `If-Match`.

Минимальная backend-валидация payload:
- root должен быть JSON array
- каждый элемент массива должен быть JSON object
- `id` должен быть непустой строкой
- `id` должен быть уникален в пределах массива

### 3.4 `GET /api/admin/configs/{name}/history?environment=dev`

Возвращает историю версий секции.

Response `200`:

```json
[
  {
    "version": 13,
    "etag": "a1b2c3",
    "updatedBy": "gd_alex",
    "updatedAt": "2026-06-08T10:00:00Z",
    "comment": "balance pass"
  },
  {
    "version": 12,
    "etag": "99ff00",
    "updatedBy": "gd_kate",
    "updatedAt": "2026-06-07T18:20:00Z",
    "comment": "initial import"
  }
]
```

Важно:
- `/history` является metadata-only endpoint
- полный `json` версий через `/history` не возвращается

### 3.5 `GET /api/admin/configs/{name}/versions/{version}?environment=dev`

Возвращает конкретную историческую версию секции.

Response `200`:

```json
{
  "name": "books",
  "environment": "dev",
  "version": 12,
  "etag": "99ff00",
  "json": [
    {
      "id": "book_dune",
      "title": "Dune"
    }
  ]
}
```

Responses:
- `200 OK` + `AdminConfigDto`
- `404 Not Found`, если такой версии нет в указанном environment
- `400 Bad Request`, если `environment` не передан

Важно:
- endpoint не выставляет HTTP header `ETag`
- при необходимости `etag` нужно брать из body
- endpoint read-only и не создает новую версию
- основной use case: lazy-load одной исторической версии для просмотра или diff

### 3.6 `POST /api/admin/configs/{name}/rollback?environment=dev&to=12`

Создает новую текущую версию, содержимое которой совпадает с указанной исторической версией.

Responses:
- `200 OK` + `AdminConfigDto` новой версии
- `404 Not Found`, если target version не существует
- `400 Bad Request`, если обязательные query-параметры не переданы

Rollback работает append-only: старая версия не удаляется.

### 3.7 `POST /api/admin/configs/{name}/promote?from=dev&to=prod`

Копирует текущее содержимое секции из одного environment в другой через создание новой версии в target environment.

Responses:
- `200 OK` + `AdminConfigDto` новой версии в target environment
- `404 Not Found`, если source section отсутствует
- `400 Bad Request`, если `from` или `to` не переданы

В текущем MVP отдельный promote endpoint сохраняется и является частью контракта.

## 4. Environments

- Public API:
  - `environment` optional
  - default value: `prod`
- Admin API:
  - `environment` required для `GET`, `PUT`, `history`, `versions`, `rollback`
  - `from` и `to` required для `promote`
- Backend не использует hardcoded whitelist environments; значения остаются строковыми (`dev`, `prod`, `staging`, ...).

## 5. ETag и versioning

- `version` монотонно растет в пределах `(name, environment)`.
- `etag` основан на содержимом секции, а не просто на номере версии.
- Повторная публикация идентичного содержимого может дать новый `version`, но тот же `etag`.
- Инвариант для current public snapshot:

```text
manifest[name].etag == ETag from GET /api/v1/configs/{name}
```

Использование заголовков:
- Public current config: `If-None-Match` / `ETag`
- Admin current config: `If-Match` для publish, `ETag` на `GET current`
- Admin historical version: без HTTP `ETag`, только `etag` в body

## 6. Storage model

Минимальная модель таблицы:

```sql
configs(
  name        text        not null,
  environment text        not null,
  version     bigint      not null,
  etag        text        not null,
  json        jsonb       not null,
  updated_by  text        not null,
  updated_at  timestamptz not null,
  comment     text,
  primary key (name, environment, version)
);
```

Принципы:
- publish, rollback и promote работают через append-only вставку новой версии
- current version определяется как latest `version`
- optimistic concurrency проверяется до вставки новой версии

## 7. Практический сценарий admin-редактирования

1. `GET /api/admin/configs/books?environment=dev`
2. Клиент сохраняет `etag` текущей версии.
3. Клиент меняет локальную копию полного массива книг.
4. `PUT /api/admin/configs/books?environment=dev` с полным новым массивом и `If-Match: "<etag>"`
5. Если пришел `412`, нужно заново сделать `GET current`, смержить изменения и повторить `PUT`.

Важно:
- нельзя добавить одну книгу отдельным частичным запросом
- всегда публикуется весь snapshot секции

## 8. Checklist

- [x] Public manifest и public config работают по `/api/v1/configs/*`
- [x] Public `environment` optional, fallback в `prod`
- [x] Admin routes идут по `/api/admin/configs/*` без `v1`
- [x] Admin auth - Basic
- [x] `PUT` использует `If-Match` и возвращает `412` на stale concurrency
- [x] `/history` возвращает только метаданные
- [x] `/versions/{version}` возвращает snapshot конкретной версии без HTTP `ETag`
- [x] rollback и promote создают новые append-only версии
