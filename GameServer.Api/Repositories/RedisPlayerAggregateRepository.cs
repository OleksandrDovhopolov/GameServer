using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisPlayerAggregateRepository : IPlayerAggregateRepository
{
    private const string ResourcesPropertyName = "Resources";
    private const string InventoryPropertyName = "Inventory";
    private const string InventoryItemsPropertyName = "InventoryItems";
    private const string ServerRewardsPropertyName = "ServerRewards";

    private static readonly IReadOnlyDictionary<string, int> DefaultResources = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Gold"] = 0,
        ["Energy"] = 0,
        ["Gems"] = 0
    };

    private readonly IDatabase _database;
    private readonly ILogger<RedisPlayerAggregateRepository> _logger;

    public RedisPlayerAggregateRepository(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisPlayerAggregateRepository> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var blobKey = PlayerSaveRedisKeys.Save(playerId);
        var metaKey = PlayerSaveRedisKeys.Meta(playerId);
        var resourcesKey = PlayerSaveRedisKeys.Resources(playerId);
        var resourcesVersionKey = PlayerSaveRedisKeys.ResourcesVersion(playerId);
        var inventoryKey = PlayerSaveRedisKeys.Inventory(playerId);
        var inventoryVersionKey = PlayerSaveRedisKeys.InventoryVersion(playerId);

        var blobTask = _database.StringGetAsync(blobKey);
        var metaTask = _database.StringGetAsync(metaKey);
        var resourcesTask = _database.StringGetAsync(resourcesKey);
        var resourcesVersionTask = _database.StringGetAsync(resourcesVersionKey);
        var inventoryTask = _database.StringGetAsync(inventoryKey);
        var inventoryVersionTask = _database.StringGetAsync(inventoryVersionKey);
        await Task.WhenAll(blobTask, metaTask, resourcesTask, resourcesVersionTask, inventoryTask, inventoryVersionTask).WaitAsync(ct);

        var blobValue = blobTask.Result;
        var blobRoot = TryParseJsonObject(blobValue.IsNull ? null : blobValue.ToString()) ?? [];
        SanitizeBlobRoot(blobRoot);

        var resources = DeserializeMap(resourcesTask.Result, DefaultResources);
        var inventory = DeserializeMap(inventoryTask.Result, new Dictionary<string, int>(StringComparer.Ordinal));

        var aggregate = new PlayerAggregate
        {
            PlayerId = playerId,
            RootDocument = blobRoot,
            LastModified = TryParseLong(metaTask.Result, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            Resources = new Dictionary<string, int>(resources, StringComparer.Ordinal),
            InventoryItems = new Dictionary<string, int>(inventory, StringComparer.Ordinal)
        };

        var blobMissing = blobValue.IsNull;
        var resourcesMissing = resourcesTask.Result.IsNull || resourcesVersionTask.Result.IsNull;
        var inventoryMissing = inventoryTask.Result.IsNull || inventoryVersionTask.Result.IsNull;
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
        SanitizeBlobRoot(blobRoot);
        var blobPayload = blobRoot.ToJsonString();

        var resourcesVersion = await GetNextVersionAsync(PlayerSaveRedisKeys.ResourcesVersion(player.PlayerId), ct);
        var inventoryVersion = await GetNextVersionAsync(PlayerSaveRedisKeys.InventoryVersion(player.PlayerId), ct);

        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Save(player.PlayerId), blobPayload);
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Meta(player.PlayerId), now.ToString(CultureInfo.InvariantCulture));
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Resources(player.PlayerId), JsonSerializer.Serialize(player.Resources));
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.ResourcesVersion(player.PlayerId), resourcesVersion.ToString(CultureInfo.InvariantCulture));
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Inventory(player.PlayerId), JsonSerializer.Serialize(player.InventoryItems));
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.InventoryVersion(player.PlayerId), inventoryVersion.ToString(CultureInfo.InvariantCulture));

        var committed = await transaction.ExecuteAsync().WaitAsync(ct);
        if (!committed)
        {
            throw new InvalidOperationException($"Redis transaction failed while saving aggregate for player '{player.PlayerId}'.");
        }

        player.LastModified = now;
    }

    public async Task DeleteAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var transaction = _database.CreateTransaction();
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Save(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Meta(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Resources(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.ResourcesVersion(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Inventory(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.InventoryVersion(playerId));
        _ = await transaction.ExecuteAsync().WaitAsync(ct);
    }

    public static void SanitizeBlobRoot(JsonObject root)
    {
        root.Remove(ResourcesPropertyName);
        root.Remove(ServerRewardsPropertyName);
        root.Remove(InventoryItemsPropertyName);
        root.Remove(InventoryPropertyName);
    }

    private async Task<long> GetNextVersionAsync(RedisKey versionKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var existing = await _database.StringGetAsync(versionKey).WaitAsync(ct);
        var current = TryParseLong(existing, 0L);
        return checked(current + 1);
    }

    private static Dictionary<string, int> DeserializeMap(RedisValue raw, IReadOnlyDictionary<string, int> fallback)
    {
        if (raw.IsNull)
        {
            return new Dictionary<string, int>(fallback, StringComparer.Ordinal);
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(raw!);
            return map is null
                ? new Dictionary<string, int>(fallback, StringComparer.Ordinal)
                : new Dictionary<string, int>(map, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, int>(fallback, StringComparer.Ordinal);
        }
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

    private static long TryParseLong(RedisValue rawValue, long fallback)
    {
        return long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

}
