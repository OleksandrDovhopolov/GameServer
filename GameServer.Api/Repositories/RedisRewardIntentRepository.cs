using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisRewardIntentRepository : IRewardIntentRepository
{
    private const string PendingByExpiryKey = "reward:intent:pending_by_expiry";
    private const long CallbackLockTtlMs = 60_000;

    private const string RewardIntentIdField = "reward_intent_id";
    private const string PlayerIdField = "player_id";
    private const string RewardIdField = "reward_id";
    private const string StatusField = "status";
    private const string CreatedAtField = "created_at_utc";
    private const string UpdatedAtField = "updated_at_utc";
    private const string ExpiresAtField = "expires_at_utc";
    private const string FulfilledAtField = "fulfilled_at_utc";
    private const string ErrorCodeField = "error_code";
    private const string ErrorMessageField = "error_message";
    private const string AdNetworkField = "ad_network";
    private const string ExternalEventIdField = "external_event_id";

    private const string CallbackStateProcessing = "processing";
    private const string CallbackStateSucceeded = "succeeded";
    private const string CallbackStateFailed = "failed";

    private const string ExpireIfPendingLua = """
                                             local intentKey = KEYS[1]
                                             local pendingIndexKey = KEYS[2]
                                             local nowMs = tonumber(ARGV[1])
                                             local rewardIntentId = ARGV[2]
                                             local errorCode = ARGV[3]
                                             local errorMessage = ARGV[4]

                                             if redis.call('EXISTS', intentKey) == 0 then
                                               return 0
                                             end

                                             local status = redis.call('HGET', intentKey, 'status')
                                             if status ~= 'pending' then
                                               return 0
                                             end

                                             local expiresAt = tonumber(redis.call('HGET', intentKey, 'expires_at_utc') or '0')
                                             if expiresAt > nowMs then
                                               return 0
                                             end

                                             redis.call('HSET', intentKey,
                                               'status', 'expired',
                                               'updated_at_utc', tostring(nowMs),
                                               'error_code', errorCode,
                                               'error_message', errorMessage)
                                             redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                             return 1
                                             """;

    private const string TryBeginCallbackProcessingLua = """
                                                       local intentKey = KEYS[1]
                                                       local callbackKey = KEYS[2]
                                                       local lockKey = KEYS[3]
                                                       local pendingIndexKey = KEYS[4]

                                                       local nowMs = tonumber(ARGV[1])
                                                       local provider = ARGV[2]
                                                       local externalEventId = ARGV[3]
                                                       local rewardIntentId = ARGV[4]
                                                       local lockToken = ARGV[5]
                                                       local lockTtlMs = tonumber(ARGV[6])

                                                       local callbackState = redis.call('GET', callbackKey)
                                                       if callbackState == 'succeeded' or callbackState == 'processing' then
                                                         return { 'CALLBACK_DUPLICATE', '', '', '', 'Callback event was already processed.' }
                                                       end

                                                       if redis.call('EXISTS', intentKey) == 0 then
                                                         return { 'INTENT_NOT_FOUND', '', '', '', 'Reward intent was not found.' }
                                                       end

                                                       local status = redis.call('HGET', intentKey, 'status') or ''
                                                       local storedProvider = redis.call('HGET', intentKey, 'ad_network') or ''
                                                       local storedExternalEventId = redis.call('HGET', intentKey, 'external_event_id') or ''

                                                       if status == 'fulfilled' then
                                                         redis.call('SET', callbackKey, 'succeeded')
                                                         return { 'INTENT_ALREADY_FULFILLED', 'fulfilled', '', '', 'Reward intent was already fulfilled.' }
                                                       end

                                                       if status == 'expired' then
                                                         redis.call('SET', callbackKey, 'succeeded')
                                                         return { 'INTENT_EXPIRED', 'expired', '', '', 'Reward intent is expired.' }
                                                       end

                                                       if status == 'rejected' then
                                                         redis.call('SET', callbackKey, 'succeeded')
                                                         return { 'CALLBACK_INVALID', 'rejected', '', '', 'Reward intent is rejected.' }
                                                       end

                                                       if status == 'failed' then
                                                         if storedProvider ~= provider or storedExternalEventId ~= externalEventId then
                                                           return { 'CALLBACK_INVALID', 'failed', '', '', 'Retry requires the same provider and externalEventId.' }
                                                         end
                                                       elseif status ~= 'pending' then
                                                         return { 'CALLBACK_INVALID', status, '', '', 'Reward intent is not in a processable state.' }
                                                       end

                                                       local expiresAt = tonumber(redis.call('HGET', intentKey, 'expires_at_utc') or '0')
                                                       if expiresAt <= nowMs then
                                                         redis.call('HSET', intentKey,
                                                           'status', 'expired',
                                                           'updated_at_utc', tostring(nowMs),
                                                           'error_code', 'INTENT_EXPIRED',
                                                           'error_message', 'Reward intent expired before callback confirmation.',
                                                           'ad_network', provider,
                                                           'external_event_id', externalEventId)
                                                         redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                                         redis.call('SET', callbackKey, 'succeeded')
                                                         return { 'INTENT_EXPIRED', 'expired', '', '', 'Reward intent expired before callback confirmation.' }
                                                       end

                                                       local lockAcquired = redis.call('SET', lockKey, lockToken, 'NX', 'PX', lockTtlMs)
                                                       if not lockAcquired then
                                                         return { 'CALLBACK_DUPLICATE', status, '', '', 'Reward intent is being processed.' }
                                                       end

                                                       local playerId = redis.call('HGET', intentKey, 'player_id') or ''
                                                       local rewardId = redis.call('HGET', intentKey, 'reward_id') or ''
                                                       if playerId == '' or rewardId == '' then
                                                         if redis.call('GET', lockKey) == lockToken then
                                                           redis.call('DEL', lockKey)
                                                         end
                                                         redis.call('HSET', intentKey,
                                                           'status', 'failed',
                                                           'updated_at_utc', tostring(nowMs),
                                                           'error_code', 'REWARD_GRANT_FAILED',
                                                           'error_message', 'Reward intent payload is incomplete.',
                                                           'ad_network', provider,
                                                           'external_event_id', externalEventId)
                                                         redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                                         redis.call('SET', callbackKey, 'failed')
                                                         return { 'REWARD_GRANT_FAILED', 'failed', '', '', 'Reward intent payload is incomplete.' }
                                                       end

                                                       redis.call('HSET', intentKey,
                                                         'updated_at_utc', tostring(nowMs),
                                                         'ad_network', provider,
                                                         'external_event_id', externalEventId)
                                                       redis.call('SET', callbackKey, 'processing')

                                                       return { 'CLAIMED', status, playerId, rewardId, '' }
                                                       """;

    private const string FinalizeCallbackProcessingLua = """
                                                        local intentKey = KEYS[1]
                                                        local callbackKey = KEYS[2]
                                                        local lockKey = KEYS[3]
                                                        local pendingIndexKey = KEYS[4]

                                                        local nowMs = tonumber(ARGV[1])
                                                        local rewardIntentId = ARGV[2]
                                                        local lockToken = ARGV[3]
                                                        local finalStatus = ARGV[4]
                                                        local errorCode = ARGV[5]
                                                        local errorMessage = ARGV[6]

                                                        if redis.call('EXISTS', intentKey) == 0 then
                                                          if redis.call('GET', lockKey) == lockToken then
                                                            redis.call('DEL', lockKey)
                                                          end
                                                          return 0
                                                        end

                                                        if redis.call('GET', lockKey) ~= lockToken then
                                                          return 0
                                                        end

                                                        if finalStatus == 'fulfilled' then
                                                          redis.call('HSET', intentKey,
                                                            'status', 'fulfilled',
                                                            'updated_at_utc', tostring(nowMs),
                                                            'fulfilled_at_utc', tostring(nowMs),
                                                            'error_code', '',
                                                            'error_message', '')
                                                          redis.call('SET', callbackKey, 'succeeded')
                                                          redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                                        elseif finalStatus == 'rejected' then
                                                          redis.call('HSET', intentKey,
                                                            'status', 'rejected',
                                                            'updated_at_utc', tostring(nowMs),
                                                            'error_code', errorCode,
                                                            'error_message', errorMessage)
                                                          redis.call('SET', callbackKey, 'succeeded')
                                                          redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                                        else
                                                          redis.call('HSET', intentKey,
                                                            'status', 'failed',
                                                            'updated_at_utc', tostring(nowMs),
                                                            'error_code', errorCode,
                                                            'error_message', errorMessage)
                                                          redis.call('SET', callbackKey, 'failed')
                                                          redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                                        end

                                                        if redis.call('GET', lockKey) == lockToken then
                                                          redis.call('DEL', lockKey)
                                                        end

                                                        return 1
                                                        """;

    private readonly IDatabase _database;
    private readonly ILogger<RedisRewardIntentRepository> _logger;

    public RedisRewardIntentRepository(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisRewardIntentRepository> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> CreateAsync(RewardIntentRecord intent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = GetIntentKey(intent.RewardIntentId);
        var entries = ToHashEntries(intent);
        var transaction = _database.CreateTransaction();
        _ = transaction.AddCondition(Condition.KeyNotExists(key));
        _ = transaction.HashSetAsync(key, entries);
        _ = transaction.SortedSetAddAsync(PendingByExpiryKey, intent.RewardIntentId, intent.ExpiresAtUtc);

        return await transaction.ExecuteAsync().WaitAsync(ct);
    }

    public async Task<RewardIntentRecord?> GetByIdAsync(string rewardIntentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var entries = await _database.HashGetAllAsync(GetIntentKey(rewardIntentId)).WaitAsync(ct);
        if (entries.Length == 0)
        {
            return null;
        }

        return ToRecord(entries);
    }

    public async Task<bool> ExpireIfPendingAsync(string rewardIntentId, long nowUtcMs, string errorCode, string errorMessage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
                ExpireIfPendingLua,
                [GetIntentKey(rewardIntentId), PendingByExpiryKey],
                [nowUtcMs, rewardIntentId, errorCode, errorMessage])
            .WaitAsync(ct);

        return (long)result == 1L;
    }

    public async Task<RewardIntentConfirmResult> TryBeginCallbackProcessingAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        long nowUtcMs,
        string lockToken,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
                TryBeginCallbackProcessingLua,
                [GetIntentKey(rewardIntentId), GetCallbackKey(provider, externalEventId), GetLockKey(rewardIntentId), PendingByExpiryKey],
                [nowUtcMs, provider, externalEventId, rewardIntentId, lockToken, CallbackLockTtlMs])
            .WaitAsync(ct);

        RedisResult[] values;
        try
        {
            values = (RedisResult[])result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected callback begin script payload type.");
            return ParseBeginResult(null, null, null, null, "Unexpected callback processing result.");
        }

        return ParseBeginResult(
            values.Length > 0 ? values[0].ToString() : null,
            values.Length > 1 ? values[1].ToString() : null,
            values.Length > 2 ? values[2].ToString() : null,
            values.Length > 3 ? values[3].ToString() : null,
            values.Length > 4 ? values[4].ToString() : null);
    }

    public async Task<bool> FinalizeCallbackProcessingAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string lockToken,
        string finalStatus,
        string errorCode,
        string errorMessage,
        long nowUtcMs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
                FinalizeCallbackProcessingLua,
                [GetIntentKey(rewardIntentId), GetCallbackKey(provider, externalEventId), GetLockKey(rewardIntentId), PendingByExpiryKey],
                [nowUtcMs, rewardIntentId, lockToken, finalStatus, errorCode, errorMessage])
            .WaitAsync(ct);

        return (long)result == 1L;
    }

    public static RewardIntentConfirmResult ParseBeginResult(
        string? resultCode,
        string? status,
        string? playerId,
        string? rewardId,
        string? errorMessage)
    {
        return new RewardIntentConfirmResult
        {
            ResultCode = string.IsNullOrWhiteSpace(resultCode) ? RewardIntentErrorCodes.RewardGrantFailed : resultCode!,
            Status = string.IsNullOrWhiteSpace(status) ? RewardIntentStatuses.Failed : status!,
            PlayerId = string.IsNullOrWhiteSpace(playerId) ? null : playerId,
            RewardId = string.IsNullOrWhiteSpace(rewardId) ? null : rewardId,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage
        };
    }

    private static string GetIntentKey(string rewardIntentId) => $"reward:intent:{rewardIntentId}";
    private static string GetCallbackKey(string provider, string externalEventId) => $"reward:callback:{provider}:{externalEventId}";
    private static string GetLockKey(string rewardIntentId) => $"reward:intent:lock:{rewardIntentId}";

    private static HashEntry[] ToHashEntries(RewardIntentRecord intent)
    {
        return
        [
            new HashEntry(RewardIntentIdField, intent.RewardIntentId),
            new HashEntry(PlayerIdField, intent.PlayerId),
            new HashEntry(RewardIdField, intent.RewardId),
            new HashEntry(StatusField, intent.Status),
            new HashEntry(CreatedAtField, intent.CreatedAtUtc),
            new HashEntry(UpdatedAtField, intent.UpdatedAtUtc),
            new HashEntry(ExpiresAtField, intent.ExpiresAtUtc),
            new HashEntry(FulfilledAtField, intent.FulfilledAtUtc?.ToString() ?? string.Empty),
            new HashEntry(ErrorCodeField, intent.ErrorCode ?? string.Empty),
            new HashEntry(ErrorMessageField, intent.ErrorMessage ?? string.Empty),
            new HashEntry(AdNetworkField, intent.AdNetwork ?? string.Empty),
            new HashEntry(ExternalEventIdField, intent.ExternalEventId ?? string.Empty)
        ];
    }

    private static RewardIntentRecord ToRecord(HashEntry[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            map[entry.Name!] = entry.Value!;
        }

        return new RewardIntentRecord
        {
            RewardIntentId = GetString(map, RewardIntentIdField),
            PlayerId = GetString(map, PlayerIdField),
            RewardId = GetString(map, RewardIdField),
            Status = GetString(map, StatusField),
            CreatedAtUtc = GetLong(map, CreatedAtField),
            UpdatedAtUtc = GetLong(map, UpdatedAtField),
            ExpiresAtUtc = GetLong(map, ExpiresAtField),
            FulfilledAtUtc = GetNullableLong(map, FulfilledAtField),
            ErrorCode = GetNullableString(map, ErrorCodeField),
            ErrorMessage = GetNullableString(map, ErrorMessageField),
            AdNetwork = GetNullableString(map, AdNetworkField),
            ExternalEventId = GetNullableString(map, ExternalEventIdField)
        };
    }

    private static string GetString(IReadOnlyDictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string? GetNullableString(IReadOnlyDictionary<string, string> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static long GetLong(IReadOnlyDictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : 0L;
    }

    private static long? GetNullableLong(IReadOnlyDictionary<string, string> map, string key)
    {
        return map.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : null;
    }
}
