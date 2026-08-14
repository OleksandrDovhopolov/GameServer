# TODO

Рабочий список задач сервера. Новые задачи добавляются в конец соответствующей категории.

Статусы: `[ ]` — todo, `[~]` — в работе, `[x]` — готово.

---

## 🔐 Аутентификация

Задачи заведены по итогам ревью коммита `ca12e2ac` («add auth»).
Порядок важен: SRV-1 закрывается до перевода `Auth__Mode` в `Optional`,
SRV-2 и SRV-3 — до `Required`.

- [ ] **SRV-1. `X-Forwarded-For` позволяет обойти rate limit на `/api/v1/auth/*`.**
  `GetClientPartitionKey` в [Program.cs](../GameServer.Api/Program.cs) берёт **первый** элемент
  заголовка `X-Forwarded-For`. Заголовок формируется по принципу «клиент → прокси дописывает
  справа», поэтому первый элемент — произвольная строка от самого клиента. Отправляя новое
  значение на каждый запрос, атакующий получает свежее ведро лимитера, и ограничение
  `AnonymousLimitPerMinute` перестаёт работать. При этом `POST /api/v1/auth/anonymous` создаёт
  строки в `players` и `player_identities`.
  Что сделать:
  - Брать **последний** элемент `X-Forwarded-For` — тот, что дописал доверенный прокси Railway.
  - Либо настроить `ForwardedHeadersOptions` с `KnownProxies` / `KnownNetworks` и партиционировать
    по `HttpContext.Connection.RemoteIpAddress` — вариант честнее, но требует знания сети Railway.
  - Тест: два запроса с разными подставными `X-Forwarded-For` попадают в одно ведро.
  - **Закрыть до перевода `Auth__Mode` в `Optional`** — на этой ступени эндпоинт становится
    реально доступен клиентам.

- [ ] **SRV-2. Повторное использование refresh-токена логируется, но не отзывает цепочку.**
  `AuthService.RefreshAsync` при неудачной ротации пишет
  `"Refresh token reuse or invalid refresh token detected"` и возвращает `invalid_refresh_token`.
  `IRefreshTokenRepository.RevokeAllForPlayerAsync` реализован в
  [PostgresAuthRepositories.cs](../GameServer.Api/Repositories/PostgresAuthRepositories.cs),
  покрыт заглушкой в тестах и **не вызывается ни разу** во всём решении.
  Следствие: при краже токена вор и владелец просто гонятся за ротацией. Проигравший получает
  401 и по клиентскому контракту вызовет `/auth/anonymous` **без** `legacyPlayerId` — то есть
  заведёт нового игрока. Для настоящего владельца устройства это выглядит как обнуление прогресса.
  Что сделать:
  - Отличать «токен не найден / протух» от «токен найден, но уже отозван» — сейчас `RotateAsync`
    в обоих случаях возвращает `null`.
  - На втором случае вызывать `RevokeAllForPlayerAsync` и логировать как инцидент безопасности.
  - Тест: ротация валидного токена дважды отзывает все токены игрока.
  - **Закрыть до перевода `Auth__Mode` в `Required`.**

- [ ] **SRV-3. Таблица `refresh_tokens` растёт без ограничений.**
  Каждое обновление вставляет новую строку и помечает старую `revoked_at`; удаления нет нигде.
  При `AccessTokenMinutes = 60` это порядка 24 строк в сутки на активного игрока. Единственная
  таблица в проекте с неограниченным ростом, и она в горячем пути авторизации.
  Что сделать:
  - Решить механизм уборки: периодическая задача, либо удаление протухших при ротации,
    либо `pg_cron` на стороне Railway.
  - Удалять строки, у которых `expires_at < now()` с запасом (например, минус 7 дней), а не
    только отозванные — отозванные нужны для детекта повторного использования из SRV-2.
  - Добавить индекс по `expires_at` в [auth_schema.sql](../GameServer.Api/Data/auth_schema.sql) —
    сейчас есть только по `player_id`, и уборка пойдёт seq scan'ом.
  - **Закрыть до перевода `Auth__Mode` в `Required`.**

- [ ] **SRV-4. Placeholder-ключ подписи JWT захардкожен в исходниках.**
  `GetJwtValidationKey` в [Program.cs](../GameServer.Api/Program.cs) при пустом
  `Auth:JwtSigningKey` подставляет константу
  `"GameServer.DisabledAuth.Placeholder.OnlyForStartup.32Bytes"`, а `ValidateAuthConfiguration`
  при `Mode = Disabled` наличие настоящего ключа не проверяет. Текущий прод именно в этом
  состоянии, то есть любой, кто видел репозиторий, может подписать валидный токен и пройти
  `[Authorize]`.
  Сегодня безвредно: единственный такой маршрут — `POST /api/v1/auth/link`, а верификаторов
  провайдеров ноль, поэтому он возвращает `provider_not_supported`. Но это мина под любой
  будущий `[Authorize]`-эндпоинт.
  Что сделать:
  - Не регистрировать `AddJwtBearer` вовсе, если ключ пуст, либо генерировать случайный ключ
    на процесс, чтобы подделать токен было невозможно.
  - Заодно решить, не должен ли `ValidateAuthConfiguration` требовать ключ и при `Disabled`
    вне Development.

- [ ] **SRV-5. Противоречивый код ошибки при входе через внешнего провайдера.**
  `AuthService.LoginWithVerifierAsync` при `account is null` возвращает
  `AuthErrorCodes.IdentityAlreadyLinked` с сообщением `"Identity is not linked to a player."` —
  код и текст противоречат друг другу, а `AuthController` мапит этот код в `409 Conflict`.
  Клиент не сможет отличить «этот Google-аккаунт впервые» (нужна регистрация) от «занят другим
  игроком» (нужно сообщение об ошибке) — а это два разных экрана.
  Сейчас недостижимо, потому что реализаций `IExternalIdentityVerifier` нет. Но код писался
  именно как задел под провайдеров, и правку дешевле внести до появления первого.
  Что сделать:
  - Ввести отдельный `AuthErrorCodes.IdentityNotLinked`, замапить его в `404` или `409`
    осознанно.
  - Зафиксировать оба кода в контракте для клиента до того, как появится кнопка «войти через
    Google».

---

## ⚡ Производительность

- [ ] **SRV-6. Один `POST /save/global` делает лишние обращения к базе.**
  Сейчас на одно сохранение приходится до четырёх round-trip'ов там, где достаточно двух:
  1. [SaveService.cs:46](../GameServer.Api/Services/SaveService.cs) → `ApplyWheelSaveGuardsAsync` →
     [:119](../GameServer.Api/Services/SaveService.cs) `_saveRepository.LoadAsync` — читает
     `root_document` и `last_modified`;
  2. [SaveService.cs:53](../GameServer.Api/Services/SaveService.cs)
     `_playerAggregateRepository.LoadAsync` — читает **тот же** `root_document` плюс
     `resources` / `inventory` и их версии;
  3. внутри этого же `LoadAsync` — ещё одна запись, если у игрока отсутствует блоб, resources
     или inventory (материализация дефолтов);
  4. [SaveService.cs:55](../GameServer.Api/Services/SaveService.cs)
     `_playerAggregateRepository.SaveAsync` — собственно запись.

  На `DualWriteRead*` каждая запись уходит ещё и в зеркало, то есть цена удваивается.

  Внутри приватной сети Railway это единицы миллисекунд и незаметно. Но стоимость каждого
  round-trip'а линейно растёт с расстоянием до игрока: при релизе в Азию из `europe-west4`
  один океан на запрос превращается в четыре. Сокращение обращений помогает **всем** регионам
  сразу и не требует ни мульти-региона, ни переноса базы.

  Что сделать:
  - Загружать аггрегат **один раз** и передавать `aggregate.RootDocument` в
    `ApplyWheelSaveGuardsAsync` вместо отдельного `_saveRepository.LoadAsync`. Гард смотрит
    только на секцию `FortuneWheel`, а `SanitizeBlobRoot`
    ([SaveService.cs:348](../GameServer.Api/Services/SaveService.cs)) вырезает лишь
    `Resources` / `Inventory` / `InventoryItems` / `ServerRewards` — то есть санированный корень
    для гарда эквивалентен сырому блобу. Проверить это утверждение тестом перед правкой.
  - Заодно `DeleteAsync` ([SaveService.cs:72](../GameServer.Api/Services/SaveService.cs)): сейчас
    `ExistsAsync` и следом `DeleteAsync` — два запроса там, где хватит одного `DELETE` с проверкой
    числа затронутых строк.
  - Не менять наблюдаемое поведение: `404` на удаление несуществующего сейва и материализация
    дефолтов при первом обращении должны остаться как есть — контрактные изменения идут
    отдельной задачей.
  - Тесты `SaveServiceTests` должны остаться зелёными без правок; добавить проверку, что
    сохранение существующего игрока делает ровно одно чтение аггрегата.

  **Делать до релиза в удалённые регионы.** Парная задача на клиенте (её здесь нет, она в
  репозитории игры): протянуть `RequestTimeoutMs` и `RetryCount` из `SaveBackendConfig` в команды
  — сейчас `HttpSaveStorage` их не передаёт, и на длинных международных маршрутах с потерями
  пакетов сохранение будет молча отваливаться в локальный кэш.
