# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build GameServer.sln

# Run API locally (requires Redis on localhost:6379)
dotnet run --project GameServer.Api

# Run all tests
dotnet test GameServer.sln

# Run a specific test class
dotnet test GameServer.Api.Tests --filter "FullyQualifiedName~PackServiceTests"

# Run a specific test method
dotnet test GameServer.Api.Tests --filter "FullyQualifiedName~PackServiceTests.OpenPackAsync_WhenWinterEventId_LoadsWinterCardDatabase"

# Start infrastructure (Redis) via Docker Compose
docker-compose up redis

# Start full stack (API + Redis)
docker-compose up
```

## Architecture

**Solution**: `GameServer.Api` (ASP.NET Core 9.0 web API) + `GameServer.Api.Tests` (xUnit).

**Primary data store**: Redis. PostgreSQL is configured but not actively used — all repositories in `Repositories/` are Redis-backed. Data flows through `IConnectionMultiplexer` (StackExchange.Redis).

**Layer structure** (all under `GameServer.Api/`):
- `Controllers/` — thin HTTP layer; maps service results to status codes
- `Services/` — all business logic; interfaces live in `Services/Interfaces/`
- `Repositories/` — Redis data access; interfaces in `Repositories/Interfaces/`
- `Models/Domain/` — internal domain types
- `Models/Dto/` — request/response shapes per feature
- `Configuration/` — strongly-typed `IOptions<T>` bindings
- `Data/` — JSON data files loaded at startup (copied to output directory)

**Key feature areas**:

- **Player Save** (`SaveController`, `RedisSaveRepository`) — raw JSON blob stored per player in Redis; legacy blob used by the Fortune Wheel admin path.
- **Resources** (`ResourcesController`, `RedisPlayerResourcesRepository`) — Gold, Gems, Energy as integer values in Redis hashes per player.
- **Inventory** (`InventoryController`, `RedisPlayerInventoryRepository`) — item stacks (card packs, lure items) in Redis per player.
- **Fortune Wheel** (`WheelController`, `WheelService`) — weighted reward selection from `Data/rewards.json`; spin regeneration tracked via player aggregate. `WheelRewardCatalog` is a singleton loaded at startup from `rewards.json`.
- **Reward System** — `RewardDefinitionRepository` loads `Data/reward_definitions.json` at startup. `RewardApplicationService` dispatches `RewardPart` items to typed handlers (`ResourceRewardHandler`, `InventoryRewardHandler`, `FreeSpinRewardHandler`). `RewardKind` is an integer enum (1=Resource, 2=InventoryItem, 7=FreeSpin).
- **Reward Intents** (`RewardIntentController`, `RewardIntentService`, `RedisRewardIntentRepository`) — async reward grant flow for ad callbacks. Intent created client-side, ad network calls the GET callback endpoint; Lua scripts in Redis ensure exactly-once processing. 30-minute TTL on pending intents.
- **Battle Pass** (`BattlePassController`, `BattlePassService`) — seasons and levels stored as JSON in Redis (`bp:season:*` keys). XP accumulation with distributed lock per claim. Free vs. Premium track; entitlements tracked via `RedisEntitlementRepository`.
- **IAP** (`IapController`, `IapService`) — Google Play purchase verification via `GooglePlayPublisherApiClient`. Disabled in Development (`GooglePlayBilling.Enabled=false`), replaced by `StubGooglePlayPurchaseVerifier`. Purchases tracked in Redis for idempotency.
- **Live Ops Schedule** — event timeline read from `Data/liveops_schedule.json` at request time. `FileLiveOpsScheduleService` resolves active event based on current UTC time. Card pack events use `EventId` to select the right card database JSON file.
- **Admin endpoints** — `[ServiceFilter(typeof(AdminBasicAuthFilter))]` gates all admin routes via Basic Auth (`AdminAuth` config section). `AdminController` routes under `/api/admin/player`; `BattlePassAdminController` and `EntitlementsAdminController` under `/api/admin/battle-pass`.

**Player aggregate** (`RedisPlayerAggregateRepository`, `PlayerAggregateDocument`) — a flexible JSON document in Redis that stores per-player state not covered by other repositories (FortuneWheel spin counts, ad spin flags). Updated directly by several services.

**Testing approach**: hand-crafted test doubles (no mocking framework). Integration-style tests that construct real service instances with fake dependencies. `AdminTestCollection` fixture used for tests that share admin state.

## Configuration

Key `appsettings.json` sections:
- `Redis:ConnectionString` — defaults to `localhost:6379`
- `ADMIN_USER` / `ADMIN_PASS` — required admin API credentials outside Development; `AdminAuth:AllowedIps` empty = any IP allowed
- `AdminTools:AllowRedisFlush` — must be explicitly enabled for the flush endpoint
- `GooglePlayBilling:Enabled` — must be `false` in Development or the app throws at startup

`Data/liveops_schedule.json` and `Data/reward_definitions.json` are loaded eagerly at startup to fail fast on bad config.
