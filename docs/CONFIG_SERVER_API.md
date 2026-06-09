# Config Server API — спека для backend

Хэндофф для backend-разработчика. Описывает публичные и admin-методы доставки игровых
конфигов, которые нужно реализовать на .NET сервере. Документ самодостаточный — читать
без доп. контекста.

- **Сервер:** `https://gameserver-production-be8b.up.railway.app/api/v1/` (тот же, что для сейвов).
- **Клиент уже готов** и ходит по этим путям — см. `Assets/Game/Features/Configs/Server/`
  (`ServerConfigSource`, `GetConfigsManifestCommand`, `GetConfigCommand`). Менять клиент не нужно;
  нужно отдать ровно те форматы, что описаны ниже.
- Формат данных везде — **JSON**. Транспорт — HTTPS, методы GET (public) и PUT/POST/GET (admin).

---

## 1. Что делает клиент (контекст)

На старте игры клиент выполняет delta-синхронизацию:

```
1. GET /configs/manifest                 → [{ name, version, etag }, ...]
2. для каждой секции сравнивает etag с локальным снапшотом (persistentDataPath/configs/)
3. если etag совпал — пропускает (ничего не качает)
4. если отличается — GET /configs/{name}  с заголовком If-None-Match: <localEtag>
       200 → берёт тело + заголовок ETag, перезаписывает снапшот
       304 → оставляет локальный снапшот
5. сеть недоступна → работает на локальном снапшоте
```

Конфиг = **JSON-массив** объектов одного типа, у каждого объекта есть поле `id`.
Сейчас в игре 4 секции: `books`, `locations`, `requests`, `events`.

> **Критично для трафика:** `etag` в манифесте для секции ДОЛЖЕН совпадать с заголовком
> `ETag`, который возвращает `GET /configs/{name}` для текущей версии. Иначе клиент будет
> перекачивать секцию при каждом запуске.

---

## 2. Public API (читает игровой клиент)

Доступ — без аутентификации (конфиги не секретны), либо за общим app-level токеном, если он
уже есть на других public-ручках. Согласовать с тем, как защищён остальной public API.

### 2.1 `GET /configs/manifest`

Список актуальных версий всех секций для окружения (см. §4).

**Response `200`:**
```json
[
  { "name": "books",     "version": 13, "etag": "a1b2c3" },
  { "name": "locations", "version": 4,  "etag": "d4e5f6" },
  { "name": "requests",  "version": 7,  "etag": "77aa11" },
  { "name": "events",    "version": 2,  "etag": "0f0f0f" }
]
```

- `name` — имя секции (= имя файла без расширения).
- `version` — целое (int64), монотонно растёт при каждой публикации секции. Информационное/для admin.
- `etag` — строка, идентификатор текущего контента секции (см. §5). Клиент сравнивает строки на равенство.

### 2.2 `GET /configs/{name}`

Контент одной секции. Пример: `GET /configs/books`.

**Request headers (опционально):**
```
If-None-Match: "a1b2c3"
```

**Response `200`** (контент изменился или заголовок не прислан):
```
ETag: "a1b2c3"
Content-Type: application/json
```
```json
[
  { "id": "book_dune",   "title": "Dune",       "author": "Frank Herbert", "genre": "sci-fi",  "basePrice": 120, "rarityWeight": 0.2 },
  { "id": "book_hobbit", "title": "The Hobbit", "author": "J.R.R. Tolkien","genre": "fantasy", "basePrice": 90,  "rarityWeight": 0.5 }
]
```

**Response `304 Not Modified`** (присланный `If-None-Match` совпал с текущим etag): тело пустое.

**Response `404`** — секции с таким `name` нет.

Тело — **массив** (а не объект-обёртка). Поля объектов соответствуют моделям клиента
(`BookConfig`, `LocationConfig`, `RequestConfig`, `EventConfig`); парсинг case-insensitive,
так что `id`/`Id` оба ок, но держим единый стиль (camelCase).

---

## 3. Admin API (editor-publish для геймдизайнеров)

Через это ГД публикует конфиги без ребилда клиента. Все методы — **только с аутентификацией**
(см. §6). Клиентское Editor-окно Pull/Publish будет написано после готовности этих методов.

### 3.1 `GET /admin/configs/{name}?environment=dev`
Текущая версия секции для редактирования (с `version` и `etag` для последующего `If-Match`).

**Response `200`:**
```
ETag: "a1b2c3"
```
```json
{ "name": "books", "environment": "dev", "version": 13, "etag": "a1b2c3", "json": [ /* ...массив конфигов... */ ] }
```

### 3.2 `PUT /admin/configs/{name}?environment=dev` — публикация
**Optimistic concurrency** (решает «двое правят один файл»): клиент шлёт etag версии,
которую он редактировал.

**Request:**
```
If-Match: "a1b2c3"
Content-Type: application/json
```
```json
{ "json": [ /* новый массив конфигов */ ], "comment": "balance pass tier 2" }
```

**Responses:**
- `200` — опубликовано. Новая `version = old+1`, новый `etag`. Тело — как в 3.1.
- `412 Precondition Failed` — на сервере уже более новая версия (кто-то опубликовал раньше).
  Клиент должен пул + мёрдж и повторить. **Не перезаписывать молча.**
- `400` — невалидный JSON / нарушение схемы (см. §7, опционально).

### 3.3 `GET /admin/configs/{name}/history?environment=dev`
**Response `200`:**
```json
[
  { "version": 13, "etag": "a1b2c3", "updatedBy": "gd_alex", "updatedAt": "2026-06-08T10:00:00Z", "comment": "balance pass tier 2" },
  { "version": 12, "etag": "99ff00", "updatedBy": "gd_kate", "updatedAt": "2026-06-07T18:20:00Z", "comment": "initial" }
]
```

### 3.4 `GET /admin/configs/{name}/versions/{version}?environment=dev`
Возвращает содержимое конкретной версии в том же формате, что и `GET /admin/configs/{name}`,
но без чтения current-версии.

**Response `200`:**
```json
{ "name": "books", "environment": "dev", "version": 12, "etag": "99ff00", "json": [ /* ...массив конфигов... */ ] }
```

**Response `404`:** версии с таким `version` нет в указанном `environment`.

Использование: lazy-load одной исторической версии для diff/view из editor UI, чтобы не тянуть
полные snapshot-ы всех версий через `/history`.

### 3.5 `POST /admin/configs/{name}/rollback?environment=dev&to=12`
Создаёт **новую** версию (14), идентичную версии 12 (не удаляет историю). Возвращает как 3.1.

### 3.6 Promote dev → prod
Вариант реализации (на выбор): отдельный `POST /admin/configs/{name}/promote?from=dev&to=prod`,
либо обычный `PUT` в `environment=prod`. Прод-публикация — с явным подтверждением на клиенте.

---

## 4. Окружения (environments)

Минимум **dev** и **prod** (желательно + **staging**). Разделение — поле `environment` в БД.

- Dev-билд клиента читает `environment=dev`, prod-билд — `prod`. Как клиент сообщает окружение
  для public-ручек — согласовать: либо отдельный путь/субдомен, либо query `?environment=`,
  либо по API-ключу билда. (Сейчас клиент шлёт без явного environment — нужно решить совместно.)
- Из редактора ГД публикует в dev/staging свободно, в prod — с подтверждением.

---

## 5. ETag и версионирование

- `version` — int64, +1 при каждой публикации секции в данном окружении.
- `etag` — стабильный идентификатор контента. Рекомендация: **хэш канонизированного JSON**
  (например, SHA-256 от минифицированного тела, первые N символов hex). Тогда повторная
  публикация идентичного контента не меняет etag и не вызывает лишних скачиваний.
  Допустимо и `etag = "v" + version`, но тогда любой re-save форсит докачку.
- Формат заголовка — стандартный HTTP ETag в кавычках: `ETag: "a1b2c3"`; клиент эхо-шлёт его в `If-None-Match`.
- **Инвариант:** `manifest[name].etag == ETag` из `GET /configs/{name}` для текущей версии.

---

## 6. Аутентификация

- **Public** (`/configs/*`): можно без auth (конфиги не секретны) либо за тем же механизмом,
  что остальной public API. Решить вместе.
- **Admin** (`/admin/configs/*`): обязательно. Минимум — bearer-токен per-ГД. Роли:
  - `editor` — запись в dev/staging;
  - `publisher` — плюс prod.
- Аудит: в каждой версии хранить `updatedBy`, `updatedAt`, `comment`.

---

## 7. Модель хранения (минимум)

Одна таблица версий (история — это строки, не перезапись):

```sql
configs(
  name        text        not null,   -- books | locations | requests | events | ...
  environment text        not null,   -- dev | staging | prod
  version     bigint      not null,   -- +1 при публикации
  etag        text        not null,   -- хэш контента (§5)
  json        jsonb       not null,   -- сам конфиг: JSON-массив
  updated_by  text        not null,
  updated_at  timestamptz not null,
  comment     text,
  primary key (name, environment, version)
);
```

- `manifest` = строки с максимальной `version` по каждому `(name, environment)`.
- `GET /configs/{name}` отдаёт `json` последней версии + её `etag`.
- (опц.) Валидация схемы при `PUT`: тело — массив, у каждого элемента непустой уникальный `id`.

---

## 8. Что НЕ требуется (отложено)

Сознательно вне MVP — не реализовывать сейчас:

- per-section CRC + бинарный delta-протокол (хватает per-file ETag/`If-None-Match`/`304`);
- MessagePack / бинарные паки (отдаём обычный JSON);
- сжатие/паки ассетов, CDN-инвалидация по версии приложения.

Эксперименты / A/B / фиче-флаги / таргетинг по аудиториям **не делаются на этом сервере** —
они уже закрыты Firebase Remote Config (override-слой поверх этих конфигов на клиенте).

---

## 9. Чеклист для backend

- [ ] `GET /configs/manifest` → массив `{name, version, etag}` (public).
- [ ] `GET /configs/{name}` с `If-None-Match` → `200`+`ETag`+массив / `304` / `404` (public).
- [ ] Инвариант `manifest.etag == GET.ETag` для текущей версии.
- [ ] `GET /admin/configs/{name}` (с version+etag), auth.
- [ ] `PUT /admin/configs/{name}` с `If-Match` → `200` / `412`, инкремент version, новый etag, запись аудита.
- [ ] `GET /admin/configs/{name}/history`.
- [ ] `GET /admin/configs/{name}/versions/{version}`.
- [ ] `POST /admin/configs/{name}/rollback?to=`.
- [ ] Окружения dev/(staging)/prod + способ выбора окружения клиентом (согласовать).
- [ ] Auth для `/admin/*` (роли editor/publisher), аудит updated_by/at/comment.
- [ ] Таблица `configs` (история версиями).

Связанные документы:
- [ADR-001-config-section-snapshot-storage.md](ADR-001-config-section-snapshot-storage.md) — почему секции хранятся как versioned snapshot целиком.
- [CONFIG_CACHE_SYSTEM.md](CONFIG_CACHE_SYSTEM.md) — клиентская архитектура целиком.
