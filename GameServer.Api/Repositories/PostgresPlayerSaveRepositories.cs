using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;
using Npgsql;

namespace GameServer.Api.Repositories;

public sealed class PostgresSaveRepository : ISaveRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSaveRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = """
            INSERT INTO player_saves (player_id, root_document, last_modified, updated_at)
            VALUES (@playerId, @data, @lastModified, now())
            ON CONFLICT (player_id) DO UPDATE
               SET root_document = EXCLUDED.root_document,
                   last_modified = EXCLUDED.last_modified,
                   updated_at = now()
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { playerId, data, lastModified },
            cancellationToken: cancellationToken));
    }

    public async Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = """
            SELECT root_document, last_modified
            FROM player_saves
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<SaveRow>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: cancellationToken));

        if (row?.root_document is null)
        {
            return null;
        }

        return new SaveRecord
        {
            Data = row.root_document,
            LastModified = row.last_modified ?? 0L
        };
    }

    public async Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = """
            SELECT last_modified
            FROM player_saves
            WHERE player_id = @playerId AND root_document IS NOT NULL
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var value = await connection.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: cancellationToken));
        return value;
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = "DELETE FROM player_saves WHERE player_id = @playerId";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM player_saves
                WHERE player_id = @playerId AND root_document IS NOT NULL
            )
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: cancellationToken));
    }

    private sealed record SaveRow(string? root_document, long? last_modified);
}

public sealed class PostgresPlayerAggregateRepository : IPlayerAggregateRepository
{
    private static readonly IReadOnlyDictionary<string, int> DefaultResources = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Gold"] = 0,
        ["Energy"] = 0,
        ["Gems"] = 0
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresPlayerAggregateRepository> _logger;

    public PostgresPlayerAggregateRepository(
        NpgsqlDataSource dataSource,
        ILogger<PostgresPlayerAggregateRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT
                root_document,
                last_modified,
                resources::text AS resources,
                resources_version,
                inventory::text AS inventory,
                inventory_version
            FROM player_saves
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<PlayerSaveRow>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));

        var blobRoot = TryParseJsonObject(row?.root_document) ?? [];
        RedisPlayerAggregateRepository.SanitizeBlobRoot(blobRoot);
        var resources = DeserializeMap(row?.resources, DefaultResources);
        var inventory = DeserializeMap(row?.inventory, new Dictionary<string, int>(StringComparer.Ordinal));

        var aggregate = new PlayerAggregate
        {
            PlayerId = playerId,
            RootDocument = blobRoot,
            LastModified = row?.last_modified ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Resources = new Dictionary<string, int>(resources, StringComparer.Ordinal),
            InventoryItems = new Dictionary<string, int>(inventory, StringComparer.Ordinal)
        };

        var blobMissing = row?.root_document is null;
        var resourcesMissing = row?.resources is null || row.resources_version is null;
        var inventoryMissing = row?.inventory is null || row.inventory_version is null;
        if (blobMissing || resourcesMissing || inventoryMissing)
        {
            _logger.LogInformation(
                "Initializing aggregate defaults for {PlayerId}. BlobMissing: {BlobMissing}, ResourcesMissing: {ResourcesMissing}, InventoryMissing: {InventoryMissing}",
                playerId,
                blobMissing,
                resourcesMissing,
                inventoryMissing);
            await SaveAsync(aggregate, ct);
        }

        return aggregate;
    }

    public async Task SaveAsync(PlayerAggregate player, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var blobRoot = player.RootDocument.DeepClone().AsObject();
        RedisPlayerAggregateRepository.SanitizeBlobRoot(blobRoot);
        var blobPayload = blobRoot.ToJsonString();
        var resourcesJson = JsonSerializer.Serialize(player.Resources);
        var inventoryJson = JsonSerializer.Serialize(player.InventoryItems);

        const string currentSql = """
            SELECT resources_version, inventory_version
            FROM player_saves
            WHERE player_id = @playerId
            FOR UPDATE
            """;

        const string upsertSql = """
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
                @blobPayload,
                @lastModified,
                @resourcesJson::jsonb,
                @resourcesVersion,
                @inventoryJson::jsonb,
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
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var current = await connection.QueryFirstOrDefaultAsync<VersionRow>(new CommandDefinition(
            currentSql,
            new { playerId = player.PlayerId },
            transaction,
            cancellationToken: ct));

        var resourcesVersion = checked((current?.resources_version ?? 0L) + 1);
        var inventoryVersion = checked((current?.inventory_version ?? 0L) + 1);

        await connection.ExecuteAsync(new CommandDefinition(
            upsertSql,
            new
            {
                playerId = player.PlayerId,
                blobPayload,
                lastModified = now,
                resourcesJson,
                resourcesVersion,
                inventoryJson,
                inventoryVersion
            },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        player.LastModified = now;
    }

    public async Task DeleteAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = "DELETE FROM player_saves WHERE player_id = @playerId";
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));
    }

    private static JsonObject? TryParseJsonObject(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(payload) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, int> DeserializeMap(string? raw, IReadOnlyDictionary<string, int> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, int>(fallback, StringComparer.Ordinal);
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(raw);
            return map is null
                ? new Dictionary<string, int>(fallback, StringComparer.Ordinal)
                : new Dictionary<string, int>(map, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, int>(fallback, StringComparer.Ordinal);
        }
    }

    private sealed record PlayerSaveRow(
        string? root_document,
        long? last_modified,
        string? resources,
        long? resources_version,
        string? inventory,
        long? inventory_version);

    private sealed record VersionRow(long? resources_version, long? inventory_version);
}

public sealed class PostgresPlayerResourcesRepository : IPlayerResourcesRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPlayerResourcesRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT resources::text AS resources, resources_version
            FROM player_saves
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<ResourcesRow>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));

        if (row?.resources is null)
        {
            return null;
        }

        return new PlayerResourcesRecord
        {
            PlayerId = playerId,
            Resources = DeserializeMap(row.resources),
            Version = row.resources_version ?? 0L
        };
    }

    public async Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            INSERT INTO player_saves (player_id, resources, resources_version, updated_at)
            VALUES (@playerId, @resourcesJson::jsonb, @version, now())
            ON CONFLICT (player_id) DO UPDATE
               SET resources = EXCLUDED.resources,
                   resources_version = EXCLUDED.resources_version,
                   updated_at = now()
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                playerId = record.PlayerId,
                resourcesJson = JsonSerializer.Serialize(record.Resources),
                version = record.Version
            },
            cancellationToken: ct));
    }

    private static Dictionary<string, int> DeserializeMap(string raw)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(raw);
            return map is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(map, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private sealed record ResourcesRow(string? resources, long? resources_version);
}

public sealed class PostgresPlayerInventoryRepository : IPlayerInventoryRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPlayerInventoryRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT inventory::text AS inventory, inventory_version
            FROM player_saves
            WHERE player_id = @playerId
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var row = await connection.QueryFirstOrDefaultAsync<InventoryRow>(new CommandDefinition(
            sql,
            new { playerId },
            cancellationToken: ct));

        if (row?.inventory is null)
        {
            return null;
        }

        return new PlayerInventoryRecord
        {
            PlayerId = playerId,
            Items = DeserializeMap(row.inventory),
            Version = row.inventory_version ?? 0L
        };
    }

    public async Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            INSERT INTO player_saves (player_id, inventory, inventory_version, updated_at)
            VALUES (@playerId, @inventoryJson::jsonb, @version, now())
            ON CONFLICT (player_id) DO UPDATE
               SET inventory = EXCLUDED.inventory,
                   inventory_version = EXCLUDED.inventory_version,
                   updated_at = now()
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                playerId = record.PlayerId,
                inventoryJson = JsonSerializer.Serialize(record.Items),
                version = record.Version
            },
            cancellationToken: ct));
    }

    private static Dictionary<string, int> DeserializeMap(string raw)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(raw);
            return map is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(map, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private sealed record InventoryRow(string? inventory, long? inventory_version);
}
