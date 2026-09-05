using System.Globalization;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisSaveRepository : ISaveRepository
{
    private readonly IDatabase _database;

    public RedisSaveRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var transaction = _database.CreateTransaction();

        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Save(playerId), data, PlayerSaveRedisKeys.Ttl);
        _ = transaction.StringSetAsync(PlayerSaveRedisKeys.Meta(playerId), lastModified.ToString(CultureInfo.InvariantCulture), PlayerSaveRedisKeys.Ttl);

        var committed = await transaction.ExecuteAsync().WaitAsync(cancellationToken);
        if (!committed)
        {
            throw new InvalidOperationException("Redis transaction failed while saving player data.");
        }
    }

    public async Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dataTask = _database.StringGetAsync(PlayerSaveRedisKeys.Save(playerId));
        var metaTask = _database.StringGetAsync(PlayerSaveRedisKeys.Meta(playerId));
        await Task.WhenAll(dataTask, metaTask).WaitAsync(cancellationToken);

        var data = dataTask.Result;
        if (data.IsNull)
        {
            return null;
        }

        var lastModified = TryParseLastModified(metaTask.Result);
        return new SaveRecord
        {
            Data = data!,
            LastModified = lastModified
        };
    }

    public async Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await ExistsAsync(playerId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var meta = await _database.StringGetAsync(PlayerSaveRedisKeys.Meta(playerId)).WaitAsync(cancellationToken);
        return meta.IsNull ? 0L : TryParseLastModified(meta);
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = _database.CreateTransaction();
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Save(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Meta(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Resources(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.ResourcesVersion(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.Inventory(playerId));
        _ = transaction.KeyDeleteAsync(PlayerSaveRedisKeys.InventoryVersion(playerId));
        _ = await transaction.ExecuteAsync().WaitAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyExistsAsync(PlayerSaveRedisKeys.Save(playerId)).WaitAsync(cancellationToken);
    }

    private static long TryParseLastModified(RedisValue rawValue)
    {
        return long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0L;
    }
}
