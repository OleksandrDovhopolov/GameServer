using System.Globalization;
using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class HybridSaveRepository : ISaveRepository
{
    private static readonly TimeSpan RedisTtl = TimeSpan.FromHours(24);
    private const string DefaultSaveData = "";

    private readonly IDatabase _database;
    private readonly RedisSaveRepository _redisRepository;
    private readonly PostgreSqlSaveRepository _postgresRepository;
    private readonly ILogger<HybridSaveRepository> _logger;

    public HybridSaveRepository(
        RedisSaveRepository redisRepository,
        PostgreSqlSaveRepository postgresRepository,
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<HybridSaveRepository> logger)
    {
        _redisRepository = redisRepository;
        _postgresRepository = postgresRepository;
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Source of truth first: only update Redis if the database write succeeded.
        await _postgresRepository.SaveAsync(playerId, data, cancellationToken);
        _logger.LogInformation("HybridSaveRepository: saved save to PostgreSQL for {PlayerId}.", playerId);

        var lastModified = await _postgresRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
        if (lastModified is null)
        {
            // Defensive fallback in case the DB row was written but meta couldn't be read.
            lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        try
        {
            await CacheToRedisAsync(playerId, data, lastModified.Value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Do not fail the request if the cache update fails; the DB is already persisted.
            _logger.LogWarning(ex, "Redis cache update failed after saving save for {PlayerId}.", playerId);
        }
    }

    public async Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1) Cache-aside: try Redis first.
        try
        {
            var redisSave = await _redisRepository.LoadAsync(playerId, cancellationToken);
            if (redisSave is not null)
            {
                _logger.LogInformation("HybridSaveRepository: Redis hit for {PlayerId}.", playerId);
                return redisSave;
            }

            _logger.LogInformation("HybridSaveRepository: Redis miss for {PlayerId}.", playerId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HybridSaveRepository: Redis unavailable for {PlayerId}; falling back to PostgreSQL.", playerId);
        }

        // 2) Fallback to Postgres.
        SaveRecord? postgresSave = null;
        var postgresFailed = false;
        try
        {
            postgresSave = await _postgresRepository.LoadAsync(playerId, cancellationToken);
            if (postgresSave is not null)
            {
                _logger.LogInformation("HybridSaveRepository: PostgreSQL hit for {PlayerId}; caching to Redis.", playerId);
                try
                {
                    await CacheToRedisAsync(playerId, postgresSave.Data, postgresSave.LastModified, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HybridSaveRepository: Redis cache update failed for {PlayerId}.", playerId);
                }
                return postgresSave;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            postgresFailed = true;
            _logger.LogWarning(ex, "HybridSaveRepository: PostgreSQL unavailable for {PlayerId}; returning default state.", playerId);
        }

        // 3) Both miss/fail cases: return default state and (best effort) create it in PostgreSQL.
        if (postgresFailed)
        {
            return new SaveRecord
            {
                Data = DefaultSaveData,
                LastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        // Postgres miss: create the default record and return it.
        await _postgresRepository.SaveAsync(playerId, DefaultSaveData, cancellationToken);
        _logger.LogInformation("HybridSaveRepository: PostgreSQL miss; created default save for {PlayerId}.", playerId);
        var createdLastModified = await _postgresRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
        var record = new SaveRecord
        {
            Data = DefaultSaveData,
            LastModified = createdLastModified ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Best-effort cache write: even if Redis is down we still fulfill the request.
        try
        {
            await CacheToRedisAsync(playerId, record.Data, record.LastModified, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HybridSaveRepository: Redis cache update failed for default save for {PlayerId}.", playerId);
        }

        return record;
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Source of truth first.
        await _postgresRepository.DeleteAsync(playerId, cancellationToken);

        // Cache invalidation best-effort.
        try
        {
            await _redisRepository.DeleteAsync(playerId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HybridSaveRepository: Redis deletion failed for {PlayerId}.", playerId);
        }
    }

    public async Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (await _redisRepository.ExistsAsync(playerId, cancellationToken))
            {
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HybridSaveRepository: Redis unavailable for ExistsAsync({PlayerId}); falling back to PostgreSQL.", playerId);
        }

        return await _postgresRepository.ExistsAsync(playerId, cancellationToken);
    }

    public async Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var redisTimestamp = await _redisRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
            if (redisTimestamp is not null)
            {
                return redisTimestamp;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HybridSaveRepository: Redis unavailable for GetLastModifiedTimestampAsync({PlayerId}); falling back to PostgreSQL.", playerId);
        }

        return await _postgresRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
    }

    private async Task CacheToRedisAsync(string playerId, string data, long lastModified, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var saveKey = GetSaveKey(playerId);
        var metaKey = GetMetaKey(playerId);

        // Save both values with the same TTL so the cache can evict cleanly.
        var setSaveTask = _database.StringSetAsync(saveKey, data, RedisTtl);
        var setMetaTask = _database.StringSetAsync(
            metaKey,
            lastModified.ToString(CultureInfo.InvariantCulture),
            RedisTtl);

        await Task.WhenAll(setSaveTask, setMetaTask).WaitAsync(cancellationToken);
    }

    private static RedisKey GetSaveKey(string playerId) => $"save:{playerId}";
    private static RedisKey GetMetaKey(string playerId) => $"save:{playerId}:meta";
}

