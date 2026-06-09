# Config Editor Window MVP Spec

Спецификация Unity Editor-окна для редактирования серверных конфигов через уже реализованный backend API.

Связанные документы:
- [CONFIG_SERVER_API.md](CONFIG_SERVER_API.md)
- [ADR-001-config-section-snapshot-storage.md](ADR-001-config-section-snapshot-storage.md)

## 1. Цель

Сделать дешёвый по реализации, но удобный для геймдизайнера Unity Editor tool, который:

- читает конфиги из server source
- показывает секцию как список объектов
- позволяет добавлять, редактировать, удалять элементы
- публикует новую версию секции целиком через существующий backend API
- скрывает от пользователя детали `ETag`, `If-Match` и snapshot-based storage

Инструмент не меняет backend API и не требует новых серверных endpoint-ов.

## 2. Scope MVP

В MVP входят:

- Unity Editor menu entry
- одно EditorWindow
- работа с секциями `books`, `locations`, `requests`, `events`
- environments: `dev`, `prod`
- pull текущей секции
- редактирование списка элементов
- add / duplicate / delete item
- publish с comment
- history просмотр
- rollback на выбранную версию
- promote `dev -> prod`
- raw JSON fallback

В MVP не входят:

- совместное редактирование
- автоматический merge конфликтов
- section-specific сложные формы для всех типов
- schema generator
- inline diff viewer с подсветкой полей
- batch publish нескольких секций

## 3. Entry Point

Добавить Unity menu item:

```text
Tools/Game Server/Config Editor
```

При нажатии открывается `ConfigEditorWindow`.

Дополнительно допустим второй путь:

```text
Window/Game Server/Config Editor
```

Если в проекте уже есть внутреннее меню tooling, использовать его стиль и namespace.

## 4. Window Layout

Окно делится на 4 зоны.

### 4.1 Toolbar

Поля и кнопки сверху:

- `Section` dropdown
  - `books`
  - `locations`
  - `requests`
  - `events`
- `Environment` dropdown
  - `dev`
  - `prod`
- `Pull`
- `Publish`
- `History`
- `Promote to Prod`
- `Raw JSON`

Справа статус:

- `Version: <n>`
- `ETag: <value>`
- `State: Clean / Dirty / Loading / Publishing / Conflict / Error`

### 4.2 Left Pane: Item List

Список элементов текущей секции.

Для каждого элемента:

- `id`
- краткое второе поле для удобства
  - для `books`: `title`
  - для других секций: первое человекочитаемое поле, если есть

Действия:

- поиск по `id`
- выбор элемента
- `Add`
- `Duplicate`
- `Delete`

### 4.3 Right Pane: Item Details

Редактор выбранного элемента.

Минимальный MVP:

- generic key/value rendering через `SerializedObject` или JSON-backed form
- поддержка строк, чисел, bool
- массивы и вложенные объекты можно показывать в foldout

Для `books` желательно сделать человеко-удобные поля:

- `id`
- `title`
- `author`
- `genre`
- `basePrice`
- `rarityWeight`

Для остальных секций допустим generic editor.

### 4.4 Bottom Panel

Нижняя панель содержит:

- поле `Publish Comment`
- read-only `Last Error`
- read-only `Last Operation Result`

## 5. Data Model Inside Editor

Окно должно хранить:

- `selectedSection`
- `selectedEnvironment`
- `currentVersion`
- `currentEtag`
- `pulledSnapshotJson`
- `workingSnapshotJson`
- `isDirty`
- `lastLoadedAt`
- `selectedItemId`
- `lastError`

Также нужно хранить список parsed items текущей секции для list view.

Snapshot model:

- `pulledSnapshotJson` — последняя версия, пришедшая с сервера
- `workingSnapshotJson` — локально редактируемая версия

`isDirty = pulledSnapshotJson != workingSnapshotJson`

## 6. Backend Contract Used by Editor

Editor использует только существующие endpoint-ы:

- `GET /api/admin/configs/{name}?environment=...`
- `PUT /api/admin/configs/{name}?environment=...`
- `GET /api/admin/configs/{name}/history?environment=...`
- `POST /api/admin/configs/{name}/rollback?environment=...&to=...`
- `POST /api/admin/configs/{name}/promote?from=dev&to=prod`

Auth:

- Basic auth
- credentials берутся из Editor settings или локального config file

Editor должен сам:

- подставлять `Authorization: Basic ...`
- хранить и передавать `If-Match`
- читать `ETag` из ответа

Пользователь не должен работать с `ETag` вручную.

## 7. Authentication UX

В окне должен быть foldout `Connection`.

Поля:

- `Base Url`
  - default: `https://gameserver-production-be8b.up.railway.app`
- `Username`
- `Password`

Кнопки:

- `Test Connection`
- `Save Locally`

Требование:

- credentials сохраняются только локально для пользователя Editor
- не коммитятся в repo

Допустимые варианты локального хранения:

- `EditorPrefs`
- локальный ignored json в `UserSettings/`

MVP recommendation:

- использовать `EditorPrefs`

## 8. Primary User Flows

### 8.1 Pull Section

Шаги:

1. Пользователь выбирает `Section` и `Environment`
2. Нажимает `Pull`
3. Editor делает `GET /api/admin/configs/{name}?environment=...`

Успех:

- обновить `currentVersion`
- обновить `currentEtag`
- сохранить response `json` в `pulledSnapshotJson`
- скопировать его в `workingSnapshotJson`
- пересобрать item list
- сбросить `isDirty = false`

Если `404`:

- показать состояние `Empty section`
- item list пустой
- `currentVersion = 0`
- `currentEtag = null`
- разрешить создать новую секцию

### 8.2 Create First Version

Если секция ещё не существует:

1. пользователь нажимает `Add`
2. создаёт минимум один элемент с корректным `id`
3. вводит `Publish Comment`
4. нажимает `Publish`

Editor должен:

- отправить `PUT`
- передать `If-Match: "bootstrap"` только если `currentEtag == null`

### 8.3 Edit Existing Section

1. Пользователь делает `Pull`
2. Меняет один или несколько элементов
3. Окно помечает состояние как `Dirty`
4. Нажимает `Publish`
5. Editor отправляет полный массив секции в body:

```json
{
  "json": [ ...full section snapshot... ],
  "comment": "..."
}
```

Header:

```text
If-Match: "<currentEtag>"
```

### 8.4 Add Item

1. Кнопка `Add`
2. Создать новый объект по шаблону
3. Выделить его в списке
4. Поставить фокус в `id`

Для `books` default template:

```json
{
  "id": "",
  "title": "",
  "author": "",
  "genre": "",
  "basePrice": 0,
  "rarityWeight": 0
}
```

### 8.5 Duplicate Item

1. Пользователь выбирает элемент
2. Нажимает `Duplicate`
3. Создаётся копия
4. `id` автоматически не копируется как есть

MVP правило:

- либо очищать `id`
- либо ставить `<oldId>_copy`

Recommended:

- очищать `id`, чтобы пользователь явно задал новый

### 8.6 Delete Item

1. Пользователь выбирает элемент
2. Нажимает `Delete`
3. Confirmation dialog
4. После подтверждения элемент удаляется из `workingSnapshotJson`

Удаление не уходит на сервер до `Publish`.

### 8.7 Publish

При нажатии `Publish` Editor:

1. выполняет локальную валидацию
2. сериализует весь snapshot
3. отправляет `PUT`
4. при `200`:
   - обновляет `currentVersion`
   - обновляет `currentEtag`
   - копирует `workingSnapshotJson` в `pulledSnapshotJson`
   - `isDirty = false`
5. при `400`:
   - показать текст ошибки backend
6. при `412`:
   - перейти в состояние `Conflict`
   - показать message:
     - `A newer version exists. Pull latest and reapply your changes.`

### 8.8 Conflict Handling

MVP conflict flow без auto-merge:

При `412` показать modal:

- `Reload Latest`
- `Cancel`

Если `Reload Latest`:

- предупредить, что локальные несохранённые изменения будут потеряны
- затем сделать `Pull`

No auto-merge в MVP.

### 8.9 View History

Кнопка `History` открывает popup/window со списком версий.

Колонки:

- `Version`
- `ETag`
- `UpdatedBy`
- `UpdatedAt`
- `Comment`

Действия:

- `View JSON`
- `Rollback`

### 8.10 Rollback

Из history user выбирает version и нажимает `Rollback`.

Editor:

1. спрашивает подтверждение
2. вызывает `POST /api/admin/configs/{name}/rollback?environment=...&to=...`
3. при успехе делает автоматический refresh current section

### 8.11 Promote to Prod

Кнопка активна только когда:

- selected environment = `dev`

При нажатии:

1. confirmation modal
2. запрос:
   - `POST /api/admin/configs/{name}/promote?from=dev&to=prod`
3. при успехе показать:
   - `Promoted to prod`

## 9. Validation Rules in Editor

Перед publish делать локальную проверку.

Обязательные MVP правила:

- root section is array
- каждый item — object
- у каждого item есть `id`
- `id` не пустой
- `id` уникален внутри секции

Для `books` дополнительно желательно:

- `title` не пустой
- `basePrice >= 0`
- `rarityWeight >= 0`

Validation errors показывать:

- внизу окна
- и рядом с item в списке, если возможно

При наличии validation errors publish disabled.

## 10. Raw JSON Mode

Кнопка `Raw JSON` переключает правую часть окна в текстовый режим.

Назначение:

- fallback для нестандартных секций
- ручная правка редких полей
- диагностика ответа сервера

Требования:

- кнопка `Format`
- кнопка `Apply JSON`
- parse validation before apply

Если raw JSON невалиден:

- не применять изменения в working state

## 11. UI States

Окно должно поддерживать явные состояния:

- `Disconnected`
- `Idle`
- `Loading`
- `Loaded`
- `Dirty`
- `Publishing`
- `Conflict`
- `Error`
- `Empty`

Минимальные переходы:

- `Pull` -> `Loading`
- successful pull -> `Loaded`
- local edit -> `Dirty`
- `Publish` -> `Publishing`
- publish success -> `Loaded`
- publish 412 -> `Conflict`
- request fail -> `Error`

## 12. Buttons Enable/Disable Rules

- `Pull`
  - enabled when credentials and section chosen
- `Publish`
  - enabled only when loaded or empty and `isDirty == true`
- `History`
  - enabled only when section loaded
- `Promote to Prod`
  - enabled only for `dev`
- `Add`
  - enabled when section loaded or empty
- `Delete`
  - enabled when item selected
- `Duplicate`
  - enabled when item selected

## 13. Tech Notes for Unity Implementation

Recommended MVP implementation:

- `EditorWindow`
- backend transport через `UnityWebRequest`
- JSON model через `Newtonsoft.Json.Linq` или существующий project serializer

Recommendation:

- использовать JSON DOM representation, а не жёстко typed models для всех секций
- typed helper разрешён только для `books`, если нужен быстрый удобный UX

Причина:

- минимизация стоимости реализации
- меньше поддержки при изменении структуры секций

## 14. Logging and Diagnostics

Окно должно логировать:

- pull request url
- publish start / success / fail
- current section/environment
- HTTP status code

Не логировать:

- password
- полный Authorization header

Дополнительно:

- кнопка `Copy Error`
- кнопка `Copy Current JSON`

## 15. Acceptance Criteria

MVP считается готовым, если:

- пользователь может открыть окно из Unity menu
- пользователь может указать URL, username, password
- пользователь может сделать `Pull` секции `books/dev`
- пользователь может добавить новую книгу и опубликовать секцию
- пользователь может изменить поле существующей книги и опубликовать секцию
- пользователь может удалить книгу и опубликовать секцию
- пользователь видит history
- пользователь может rollback на старую версию
- пользователь может promote `dev -> prod`
- `If-Match` и `ETag` полностью скрыты от пользователя
- при `412` пользователь получает понятную ошибку

## 16. Post-MVP Improvements

После MVP можно добавить:

- auto-merge по `id`
- diff view между pulled и working snapshot
- typed forms для всех секций
- bulk import/export json files
- validation schema per section
- локальный autosave draft
