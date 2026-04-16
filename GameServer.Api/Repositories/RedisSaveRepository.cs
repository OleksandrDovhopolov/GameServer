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

        _ = transaction.StringSetAsync(GetSaveKey(playerId), data);
        _ = transaction.StringSetAsync(GetMetaKey(playerId), lastModified.ToString(CultureInfo.InvariantCulture));

        var committed = await transaction.ExecuteAsync().WaitAsync(cancellationToken);
        if (!committed)
        {
            throw new InvalidOperationException("Redis transaction failed while saving player data.");
        }
    }

    public async Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dataTask = _database.StringGetAsync(GetSaveKey(playerId));
        var metaTask = _database.StringGetAsync(GetMetaKey(playerId));
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

        var meta = await _database.StringGetAsync(GetMetaKey(playerId)).WaitAsync(cancellationToken);
        return meta.IsNull ? 0L : TryParseLastModified(meta);
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = _database.CreateTransaction();
        _ = transaction.KeyDeleteAsync(GetSaveKey(playerId));
        _ = transaction.KeyDeleteAsync(GetMetaKey(playerId));
        _ = await transaction.ExecuteAsync().WaitAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyExistsAsync(GetSaveKey(playerId)).WaitAsync(cancellationToken);
    }

    private static RedisKey GetSaveKey(string playerId) => $"save:{playerId}";
    private static RedisKey GetMetaKey(string playerId) => $"save:{playerId}:meta";

    private static long TryParseLastModified(RedisValue rawValue)
    {
        return long.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0L;
    }
}
