# ADR-001: Config section snapshot storage

**Статус:** Accepted  
**Дата:** 2026-06-09  
**Связанные документы:** [CONFIG_SERVER_API.md](CONFIG_SERVER_API.md)

## Контекст

Config Server отдает игровые конфиги секциями:

- `GET /api/v1/configs/manifest`
- `GET /api/v1/configs/{name}`

Клиент читает секцию как один JSON-массив и кэширует ее через `ETag`.
Admin API публикует изменения секции, хранит историю и поддерживает rollback/promote.

Нужно было выбрать модель хранения, которая:

- сохраняет простой public contract
- поддерживает историю версий
- совместима с optimistic concurrency через `If-Match`
- не усложняет MVP лишней нормализацией данных

## Решение

Хранить каждую секцию как versioned snapshot целиком.

- одна строка в `configs` = одна версия одной секции в одном environment
- поле `json` хранит весь JSON-массив секции
- publish, rollback и promote создают новую append-only версию
- current snapshot определяется как latest version в рамках `(name, environment)`

Минимальная форма таблицы:

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

## Почему так

- Public API уже потребляет секцию целиком, а не отдельные записи.
- `ETag` нужен на уровне всей секции, и snapshot-модель этому естественно соответствует.
- История версий и rollback проще реализуются через append-only storage.
- Publish с `If-Match` проще и надежнее на уровне целой секции, чем на уровне отдельных строк.
- Для MVP это минимальная серверная сложность при предсказуемом контракте.

## Последствия

Плюсы:

- простой и стабильный контракт `manifest -> current snapshot`
- прозрачная история изменений
- простой rollback: старая версия копируется в новую текущую
- одинаковая модель для publish, rollback и promote
- естественная поддержка `ETag`, `If-None-Match` и `If-Match`

Минусы:

- нельзя частично обновить одну запись без перепубликации всей секции
- любое изменение одной книги создает новую версию всего массива `books`
- размер строки растет вместе с размером секции

## Влияние на admin workflow

Из snapshot-модели следует конкретный рабочий процесс:

1. Прочитать current version секции.
2. Изменить локальную копию полного массива.
3. Опубликовать весь snapshot секции с актуальным `If-Match`.

Если между чтением и публикацией кто-то уже создал новую версию, сервер возвращает `412 Precondition Failed`, и клиент должен:

1. перечитать current version
2. смержить локальные изменения
3. повторить publish

## Влияние на историю

- `GET /api/admin/configs/{name}/history` возвращает только метаданные версий.
- Полный snapshot исторической версии загружается отдельно через
  `GET /api/admin/configs/{name}/versions/{version}?environment=...`.
- Исторический endpoint read-only и не создает новых версий.
- Для historical version `etag` остается в body DTO; отдельный HTTP `ETag` header не используется.

Это позволяет не перегружать `/history` полными JSON snapshot-ами.

## Альтернативы, которые не выбраны

1. Хранить каждую книгу или запись отдельной строкой.
2. Разделить current-state и history по разным таблицам.
3. Хранить конфиги как файлы вне PostgreSQL.

Причины отказа:

- выше сложность сборки current snapshot и вычисления общего `etag`
- более сложный rollback и concurrency control
- больше шансов на рассинхрон между current и history
- лишняя сложность для текущего MVP

## Когда пересматривать решение

Решение стоит пересмотреть, если:

- размеры секций существенно вырастут
- появится требование к частичным server-side update отдельных элементов
- история станет настолько большой, что даже metadata flow потребует дополнительной оптимизации
