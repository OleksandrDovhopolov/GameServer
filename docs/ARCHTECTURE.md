# GameServer Architecture

This document is a living overview of how the GameServer backend is structured. It should describe the current architecture first, and planned changes only when they are clearly marked as planned.

## Purpose

GameServer is a C#/.NET backend for a mobile card game prototype. It exposes player-facing HTTP APIs for game state, rewards, inventory, live operations, battle pass, and purchases, plus admin APIs for operational and content-management workflows.

## High-Level Flow

```text
Client
  -> ASP.NET Core Controllers
  -> Application Services
  -> Repositories
  -> Redis / PostgreSQL / JSON data files
```

Controllers are intended to stay thin. Business rules live in services. Persistence details are hidden behind repository interfaces.

## Main Features

- Player cloud save: stores and loads a per-player JSON save document.
- Resource management: manages Gold, Gems, and Energy.
- Inventory management: manages item stacks such as card packs and inventory items.
- Reward system: grants typed rewards through reward definitions and reward handlers.
- Reward intents: supports exactly-once ad callback reward delivery.
- Fortune Wheel: provides weighted rewards and spin regeneration.
- Battle Pass: manages seasons, XP, levels, free/premium rewards, and entitlements.
- Google Play IAP verification: verifies purchases when billing is enabled.
- Live Ops schedule: exposes time-based game events from local JSON schedule data.
- Config Server: serves versioned config section snapshots with ETag support, history, rollback, and promote workflows.
- Admin API: exposes protected operational endpoints for player state, battle pass, entitlements, maintenance, and config management.

## Technology Stack

- .NET 9
- C#
- ASP.NET Core Web API
- Redis with StackExchange.Redis
- PostgreSQL with Npgsql and Dapper
- Docker and Docker Compose
- xUnit tests
- Swagger/OpenAPI via Swashbuckle
- JWT Bearer authentication for player auth flows
- Basic Authentication for admin endpoints
- Google Play Publisher API client

## Runtime Services

The local Docker Compose environment currently runs:

- `api`: the ASP.NET Core application container.
- `redis`: the local Redis instance used by the API.

PostgreSQL is configured in the application and used by config/auth-related repositories, but the local Compose file does not currently start a PostgreSQL container.

## Persistence Model

Redis is the primary hot storage for player game state. It stores player save data, resources, inventory, reward intents, battle pass state, entitlements, purchases, and related runtime state.

PostgreSQL is used for relational data where versioning, querying, or transactional consistency is useful. The Config Server stores each published config section as an append-only versioned snapshot.

JSON files under `GameServer.Api/Data` provide static game data such as reward definitions, card databases, and live operations schedule data.

## API Surface

Player APIs are exposed under `/api/v1/*`. They cover saves, resources, inventory, rewards, reward intents, wheel, packs, battle pass, IAP, auth, live ops, and configs.

Admin APIs are exposed under `/api/admin/*`. They are protected with Basic Authentication and cover raw player data, maintenance actions, battle pass administration, entitlements, and config publishing workflows.

## Configuration

Configuration is loaded through ASP.NET Core configuration providers and strongly typed options classes. Important sections include:

- `Redis`
- `Postgres`
- `SaveStorage`
- `Auth`
- `AdminAuth`
- `AdminTools`
- `GooglePlayBilling`

Production secrets should be supplied through environment variables or the hosting platform secret store, not committed to the repository.

## Testing

The solution includes an xUnit test project. Tests are mostly service/controller focused and use hand-written test doubles rather than a mocking framework.

## Current Messaging State

RabbitMQ/Kafka messaging is not implemented yet. The API currently performs most work synchronously inside request handling, with Redis and PostgreSQL repositories called directly from services.
