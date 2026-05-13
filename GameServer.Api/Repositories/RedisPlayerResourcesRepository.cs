using System.Globalization;
using System.Text.Json;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisPlayerResourcesRepository : IPlayerResourcesRepository
{
    private readonly IDatabase _database;

    public RedisPlayerResourcesRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var resourcesTask = _database.StringGetAsync(GetResourcesKey(playerId));
        var versionTask = _database.StringGetAsync(GetResourcesVersionKey(playerId));
        await Task.WhenAll(resourcesTask, versionTask).WaitAsync(ct);

        var resourcesValue = resourcesTask.Result;
        if (resourcesValue.IsNull)
        {
            return null;
        }

        Dictionary<string, int>? resources;
        try
        {
            resources = JsonSerializer.Deserialize<Dictionary<string, int>>(resourcesValue!);
        }
        catch
        {
            resources = null;
        }

        return new PlayerResourcesRecord
        {
            PlayerId = playerId,
            Resources = resources is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(resources, StringComparer.Ordinal),
            Version = TryParseLong(versionTask.Result)
        };
    }

    public async Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(GetResourcesKey(record.PlayerId), JsonSerializer.Serialize(record.Resources));
        _ = transaction.StringSetAsync(GetResourcesVersionKey(record.PlayerId), record.Version.ToString(CultureInfo.InvariantCulture));

        var committed = await transaction.ExecuteAsync().WaitAsync(ct);
        if (!committed)
        {
            throw new InvalidOperationException($"Redis transaction failed while saving player resources for '{record.PlayerId}'.");
        }
    }

    private static RedisKey GetResourcesKey(string playerId) => $"save:{playerId}:resources";
    private static RedisKey GetResourcesVersionKey(string playerId) => $"save:{playerId}:resources:version";

    private static long TryParseLong(RedisValue value)
    {
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
    }
}
