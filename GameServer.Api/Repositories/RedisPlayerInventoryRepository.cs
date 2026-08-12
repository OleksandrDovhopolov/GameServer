using System.Globalization;
using System.Text.Json;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisPlayerInventoryRepository : IPlayerInventoryRepository
{
    private readonly IDatabase _database;

    public RedisPlayerInventoryRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var itemsTask = _database.StringGetAsync(PlayerSaveRedisKeys.Inventory(playerId));
        var versionTask = _database.StringGetAsync(PlayerSaveRedisKeys.InventoryVersion(playerId));
        await Task.WhenAll(itemsTask, versionTask).WaitAsync(ct);

        var itemsValue = itemsTask.Result;
        if (itemsValue.IsNull)
        {
            return null;
        }

        Dictionary<string, int>? items;
        try
        {
            items = JsonSerializer.Deserialize<Dictionary<string, int>>(itemsValue!);
        }
        catch
        {
            items = null;
        }

        return new PlayerInventoryRecord
        {
            PlayerId = playerId,
            Items = items is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(items, StringComparer.Ordinal),
            Version = TryParseLong(versionTask.Result)
        };
    }

    public async Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Inventory(record.PlayerId), JsonSerializer.Serialize(record.Items));
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.InventoryVersion(record.PlayerId), record.Version.ToString(CultureInfo.InvariantCulture));

        var committed = await transaction.ExecuteAsync().WaitAsync(ct);
        if (!committed)
        {
            throw new InvalidOperationException($"Redis transaction failed while saving player inventory for '{record.PlayerId}'.");
        }
    }

    private static long TryParseLong(RedisValue value)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
    }
}
