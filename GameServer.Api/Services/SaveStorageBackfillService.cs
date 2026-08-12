using Dapper;
using GameServer.Api.Configuration;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Repositories;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace GameServer.Api.Services;

public sealed class SaveStorageBackfillService : ISaveStorageBackfillService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly NpgsqlDataSource _dataSource;
    private readonly SaveStorageOptions _options;
    private readonly ILogger<SaveStorageBackfillService> _logger;

    public SaveStorageBackfillService(
        IConnectionMultiplexer redis,
        NpgsqlDataSource dataSource,
        IOptions<SaveStorageOptions> options,
        ILogger<SaveStorageBackfillService> logger)
    {
        _redis = redis;
        _dataSource = dataSource;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SaveStorageBackfillResponse> BackfillAsync(bool overwrite, CancellationToken ct = default)
    {
        var playerIds = await ScanPlayerIdsAsync(ct);
        var written = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var playerId in playerIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var snapshot = await ReadRedisSnapshotAsync(playerId, ct);
                var changed = await UpsertSnapshotAsync(snapshot, overwrite, ct);
                if (changed)
                {
                    written++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                var message = $"{playerId}: {ex.Message}";
                errors.Add(message);
                _logger.LogError(ex, "Player save backfill failed for {PlayerId}.", playerId);
            }
        }

        return new SaveStorageBackfillResponse
        {
            Success = failed == 0,
            Scanned = playerIds.Count,
            Written = written,
            Skipped = skipped,
            Failed = failed,
            Errors = errors
        };
    }

    public async Task<SaveStorageStatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        var redisPlayers = await ScanPlayerIdsAsync(ct);

        const string sql = "SELECT count(*) FROM player_saves";
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var postgresRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            cancellationToken: ct));

        return new SaveStorageStatusResponse
        {
            Provider = _options.Provider.ToString(),
            RedisPlayers = redisPlayers.Count,
            PostgresRows = postgresRows
        };
    }

    private async Task<List<string>> ScanPlayerIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in _redis.GetEndPoints())
        {
            ct.ThrowIfCancellationRequested();

            var server = _redis.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: PlayerSaveRedisKeys.ScanPattern).WithCancellation(ct))
            {
                if (PlayerSaveRedisKeys.TryGetPlayerId(key.ToString(), out var playerId))
                {
                    ids.Add(playerId);
                }
            }
        }

        return ids.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private async Task<PlayerSaveSnapshot> ReadRedisSnapshotAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var database = _redis.GetDatabase();
        var blobTask = database.StringGetAsync(PlayerSaveRedisKeys.Save(playerId));
        var metaTask = database.StringGetAsync(PlayerSaveRedisKeys.Meta(playerId));
        var resourcesTask = database.StringGetAsync(PlayerSaveRedisKeys.Resources(playerId));
        var resourcesVersionTask = database.StringGetAsync(PlayerSaveRedisKeys.ResourcesVersion(playerId));
        var inventoryTask = database.StringGetAsync(PlayerSaveRedisKeys.Inventory(playerId));
        var inventoryVersionTask = database.StringGetAsync(PlayerSaveRedisKeys.InventoryVersion(playerId));

        await Task.WhenAll(blobTask, metaTask, resourcesTask, resourcesVersionTask, inventoryTask, inventoryVersionTask).WaitAsync(ct);

        return new PlayerSaveSnapshot(
            playerId,
            ToNullableString(blobTask.Result),
            ToNullableLong(metaTask.Result),
            ToNullableString(resourcesTask.Result),
            ToNullableLong(resourcesVersionTask.Result),
            ToNullableString(inventoryTask.Result),
            ToNullableLong(inventoryVersionTask.Result));
    }

    private async Task<bool> UpsertSnapshotAsync(PlayerSaveSnapshot snapshot, bool overwrite, CancellationToken ct)
    {
        const string insertOnlySql = """
            INSERT INTO player_saves (
                player_id,
                root_document,
                last_modified,
                resources,
                resources_version,
                inventory,
                inventory_version,
                updated_at)
            VALUES (
                @playerId,
                @rootDocument,
                @lastModified,
                @resources::jsonb,
                @resourcesVersion,
                @inventory::jsonb,
                @inventoryVersion,
                now())
            ON CONFLICT (player_id) DO NOTHING
            """;

        const string overwriteSql = """
            INSERT INTO player_saves (
                player_id,
                root_document,
                last_modified,
                resources,
                resources_version,
                inventory,
                inventory_version,
                updated_at)
            VALUES (
                @playerId,
                @rootDocument,
                @lastModified,
                @resources::jsonb,
                @resourcesVersion,
                @inventory::jsonb,
                @inventoryVersion,
                now())
            ON CONFLICT (player_id) DO UPDATE
               SET root_document = EXCLUDED.root_document,
                   last_modified = EXCLUDED.last_modified,
                   resources = EXCLUDED.resources,
                   resources_version = EXCLUDED.resources_version,
                   inventory = EXCLUDED.inventory,
                   inventory_version = EXCLUDED.inventory_version,
                   updated_at = now()
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            overwrite ? overwriteSql : insertOnlySql,
            new
            {
                playerId = snapshot.PlayerId,
                rootDocument = snapshot.RootDocument,
                lastModified = snapshot.LastModified,
                resources = snapshot.Resources,
                resourcesVersion = snapshot.ResourcesVersion,
                inventory = snapshot.Inventory,
                inventoryVersion = snapshot.InventoryVersion
            },
            cancellationToken: ct));

        return affected > 0;
    }

    private static string? ToNullableString(RedisValue value) =>
        value.IsNull ? null : value.ToString();

    private static long? ToNullableLong(RedisValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        return long.TryParse(value.ToString(), out var parsed) ? parsed : 0L;
    }

    private sealed record PlayerSaveSnapshot(
        string PlayerId,
        string? RootDocument,
        long? LastModified,
        string? Resources,
        long? ResourcesVersion,
        string? Inventory,
        long? InventoryVersion);
}
