using GameServer.Api.Services.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Services;

public sealed class RedisAdminClient : IRedisAdminClient
{
    private readonly IDatabase _database;

    public RedisAdminClient(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task FlushDatabaseAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ = await _database.ExecuteAsync("FLUSHDB").WaitAsync(ct);
    }
}
