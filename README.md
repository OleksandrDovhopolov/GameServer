# GameServer
PetGameServer

## Hybrid Save System (PostgreSQL + Redis) - Manual Verification

### 1) Start dependencies
1. Run from the `GameServer` folder:
   - `docker compose up --build`
2. Confirm all containers are up: `api`, `redis`, `postgres`.

### 2) First-time `Load` creates PostgreSQL record and returns default
1. Call:
   - `GET http://localhost:5000/api/v1/save/global?playerId=test-player-1`
2. Expect `200 OK` with:
   - `Data` equals `""` (empty string)
   - `LastModified` is a unix-millis value
3. Verify the PostgreSQL row exists:
   - `docker compose exec postgres psql -U postgres -d game_server -c "select player_id, last_modified_at from player_saves where player_id = 'test-player-1';"`

### 3) Subsequent `Load` hits Redis (via logs)
1. Call the same `GET` again.
2. Check API logs for `HybridSaveRepository: Redis hit for test-player-1`.

### 4) `Save` updates PostgreSQL and refreshes Redis (TTL)
1. Call:
   - `POST http://localhost:5000/api/v1/save/global`
2. Body example:
```json
{"playerId":"test-player-1","data":"{\"foo\":\"bar\"}"}
```
3. Verify PostgreSQL row was updated:
   - `docker compose exec postgres psql -U postgres -d game_server -c "select player_id, save_data, last_modified_at from player_saves where player_id = 'test-player-1';"`
4. Verify Redis keys were updated:
   - Run a Redis CLI (optional): `docker compose exec redis redis-cli`
   - Check:
     - `GET save:test-player-1`
     - `GET save:test-player-1:meta`

### Notes
- Hybrid load returns default state if both Redis and PostgreSQL misses/failures occur.
- Player save and metadata keys are persisted without expiration.

## Google Play Battle Pass verify

- Backend verify endpoint stays `POST /api/v1/iap/google/verify` with `playerId`, `productId`, `purchaseToken`, `seasonId`.
- Real Google Play verification is disabled by default in Development. Production-like environments must enable it.
- Required config:
  - `GooglePlayBilling__Enabled=true`
  - `GooglePlayBilling__PackageName=<android applicationId>`
  - `GooglePlayBilling__ServiceAccountJson=<full JSON key as one secret variable>` or `GooglePlayBilling__ServiceAccountKeyFile=/run/secrets/google-play-service-account.json`
- Railway recommendation:
  - Prefer `GooglePlayBilling__ServiceAccountJson` as a secret variable
  - No volume is required when JSON is passed directly in env
- Local docker example:
  - Set `GOOGLE_PLAY_BILLING__ENABLED=true`
  - Set `GOOGLE_PLAY_BILLING__PACKAGE_NAME=<android applicationId>`
  - Set `GOOGLE_PLAY_BILLING__SERVICE_ACCOUNT_JSON=<full JSON key>`
  - Optionally use `GOOGLE_PLAY_BILLING__SERVICE_ACCOUNT_KEY_FILE` with a mounted file instead
- Google Play Console setup:
  - Add the service account in `Users & permissions`
  - Grant `View financial data, orders, and cancellation survey responses`
  - Grant `Manage orders and subscriptions`
  - Ensure the battle pass one-time products are active and published
  - Ensure the tester is in both the testing track and `Settings > License testing`
