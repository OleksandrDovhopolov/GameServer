# GameServer

C#/.NET backend for a mobile card game prototype.

## Features

- Player cloud save (JSON blob per player)
- Resource management (Gold, Gems, Energy)
- Inventory system (card packs, items)
- Fortune Wheel with weighted rewards and spin regeneration
- Reward Intents — exactly-once ad-network callback flow
- Battle Pass (seasons, XP, levels, free/premium tracks)
- Google Play IAP verification
- Live Ops schedule (time-based card collection events)
- Admin API (Basic Auth protected)
- Docker Compose local environment
- xUnit tests

## Tech Stack

- C# / .NET 9
- ASP.NET Core
- Redis (primary data store)
- PostgreSQL + Dapper (configured, not yet active)
- Google Play Publisher API
- Docker Compose
- xUnit

## Architecture

```
Client → API Controllers → Service Layer → Redis
                                        ↘ Data/*.json (reward/card/schedule definitions)
```

Admin endpoints require Basic Auth (`AdminAuth` config section).  
Google Play billing is disabled in Development (`GooglePlayBilling:Enabled=false`).

## Local Setup

**Prerequisites:** .NET 9 SDK, Docker

```bash
# Start Redis
docker-compose up redis

# Run API (Swagger at http://localhost:5000/swagger)
dotnet run --project GameServer.Api

# Or run full stack
docker-compose up
```

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Redis:ConnectionString` | `localhost:6379` | Required |
| `AdminAuth:Username` | `admin` | Admin API access |
| `AdminAuth:Password` | `admin` | Admin API access |
| `AdminTools:AllowRedisFlush` | `false` | Enable flush endpoint |
| `GooglePlayBilling:Enabled` | `false` | Must be `false` in Development |
| `GooglePlayBilling:PackageName` | — | Android application ID |
| `GooglePlayBilling:ServiceAccountJson` | — | Full JSON key as env variable |

## Google Play IAP Setup

Endpoint: `POST /api/v1/iap/google/verify` — accepts `playerId`, `productId`, `purchaseToken`, `seasonId`.

Real verification is disabled by default. To enable:

```
GooglePlayBilling__Enabled=true
GooglePlayBilling__PackageName=<android applicationId>
GooglePlayBilling__ServiceAccountJson=<full JSON key>
```

Google Play Console requirements:
- Add service account under **Users & permissions**
- Grant **View financial data** and **Manage orders and subscriptions**
- Ensure products are active and published
- Tester must be in the testing track and **Settings > License testing**

## API Overview

### Player (`/api/v1/`)

| Method | Path | Description |
|---|---|---|
| GET | `/ping` | Health check |
| POST | `/save/global` | Save player data |
| GET | `/save/global` | Load player data |
| POST | `/resources/adjust` | Add/subtract Gold, Gems, Energy |
| POST | `/inventory/remove` | Remove item from inventory |
| POST | `/rewards/grant` | Grant reward by ID |
| POST | `/rewards/intent/create` | Create reward intent (before ad) |
| GET | `/rewards/intent/callback` | Ad network callback (delivers reward) |
| GET | `/wheel/data` | Spin state and available spins |
| POST | `/wheel/spin` | Spin the fortune wheel |
| POST | `/packs/open` | Open a card pack |
| GET | `/battle-pass/current` | Current season and player state |
| POST | `/battle-pass/claim` | Claim reward at level |
| POST | `/battle-pass/xp/add` | Add XP |
| POST | `/iap/google/verify` | Verify Google Play purchase |
| GET | `/liveops/schedule` | Active event schedule |

### Admin (`/api/admin/`, Basic Auth)

| Method | Path | Description |
|---|---|---|
| GET | `/player/{id}` | Raw player save |
| DELETE | `/player/{id}` | Delete player data |
| POST | `/player/{id}/spins` | Adjust fortune wheel spins |
| POST | `/inventory/add` | Add item to inventory |
| POST | `/battle-pass/seasons` | Create season |
| PUT | `/battle-pass/seasons/{id}/levels` | Set season levels |
| POST | `/battle-pass/seasons/{id}/publish` | Publish season |
| POST | `/entitlements/grant` | Grant premium pass |
| POST | `/test/redis/flush` | Flush Redis (disabled by default) |

## Running Tests

```bash
dotnet test GameServer.sln

# Single test class
dotnet test GameServer.Api.Tests --filter "FullyQualifiedName~BattlePassServiceTests"
```
