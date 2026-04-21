using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisRewardIntentRepository : IRewardIntentRepository
{
    private const string PendingByExpiryKey = "reward:intent:pending_by_expiry";

    private const string RewardIntentIdField = "reward_intent_id";
    private const string PlayerIdField = "player_id";
    private const string RewardIdField = "reward_id";
    private const string StatusField = "status";
    private const string CreatedAtField = "created_at_utc";
    private const string UpdatedAtField = "updated_at_utc";
    private const string ExpiresAtField = "expires_at_utc";
    private const string FulfilledAtField = "fulfilled_at_utc";
    private const string NewCrystalsBalanceField = "new_crystals_balance";
    private const string ErrorCodeField = "error_code";
    private const string ErrorMessageField = "error_message";
    private const string AdNetworkField = "ad_network";
    private const string ExternalEventIdField = "external_event_id";

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

    private const string ConfirmCallbackLua = """
                                       local intentKey = KEYS[1]
                                       local callbackKey = KEYS[2]
                                       local pendingIndexKey = KEYS[3]

                                       local nowMs = tonumber(ARGV[1])
                                       local provider = ARGV[2]
                                       local externalEventId = ARGV[3]
                                       local rewardIntentId = ARGV[4]
                                       local expectedRewardId = ARGV[5]
                                       local rewardAmount = tonumber(ARGV[6])

                                       if redis.call('EXISTS', callbackKey) == 1 then
                                         return { 'CALLBACK_DUPLICATE', '', '', '' }
                                       end

                                       if redis.call('EXISTS', intentKey) == 0 then
                                         return { 'INTENT_NOT_FOUND', '', '', 'Reward intent was not found.' }
                                       end

                                       local status = redis.call('HGET', intentKey, 'status') or ''
                                       local currentBalance = redis.call('HGET', intentKey, 'new_crystals_balance') or ''

                                       if status == 'fulfilled' then
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         return { 'INTENT_ALREADY_FULFILLED', status, currentBalance, 'Reward intent was already fulfilled.' }
                                       end

                                       if status == 'expired' then
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         return { 'INTENT_EXPIRED', status, currentBalance, 'Reward intent is expired.' }
                                       end

                                       if status == 'rejected' or status == 'failed' then
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         return { 'CALLBACK_INVALID', status, currentBalance, 'Reward intent is not in pending status.' }
                                       end

                                       if status ~= 'pending' then
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         return { 'CALLBACK_INVALID', status, currentBalance, 'Reward intent is not in pending status.' }
                                       end

                                       local rewardId = redis.call('HGET', intentKey, 'reward_id')
                                       if rewardId ~= expectedRewardId then
                                         redis.call('HSET', intentKey,
                                           'status', 'rejected',
                                           'updated_at_utc', tostring(nowMs),
                                           'error_code', 'CALLBACK_INVALID',
                                           'error_message', 'Reward id is not supported for callback confirmation.',
                                           'ad_network', provider,
                                           'external_event_id', externalEventId)
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                         return { 'CALLBACK_INVALID', 'rejected', '', 'Reward id is not supported for callback confirmation.' }
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
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                         return { 'INTENT_EXPIRED', 'expired', '', 'Reward intent expired before callback confirmation.' }
                                       end

                                       local playerId = redis.call('HGET', intentKey, 'player_id')
                                       if not playerId or playerId == '' then
                                         redis.call('HSET', intentKey,
                                           'status', 'failed',
                                           'updated_at_utc', tostring(nowMs),
                                           'error_code', 'REWARD_GRANT_FAILED',
                                           'error_message', 'Player id is missing on reward intent.',
                                           'ad_network', provider,
                                           'external_event_id', externalEventId)
                                         redis.call('SET', callbackKey, rewardIntentId)
                                         redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                         return { 'REWARD_GRANT_FAILED', 'failed', '', 'Player id is missing on reward intent.' }
                                       end

                                       local resourcesKey = 'save:' .. playerId .. ':resources'
                                       local resourcesVersionKey = 'save:' .. playerId .. ':resources:version'
                                       local rawResources = redis.call('GET', resourcesKey)
                                       local resources = { Gold = 0, Energy = 0, Gems = 0 }

                                       if rawResources and rawResources ~= '' then
                                         local decodeOk, decoded = pcall(cjson.decode, rawResources)
                                         if decodeOk and type(decoded) == 'table' then
                                           resources = decoded
                                         else
                                           redis.call('HSET', intentKey,
                                             'status', 'failed',
                                             'updated_at_utc', tostring(nowMs),
                                             'error_code', 'REWARD_GRANT_FAILED',
                                             'error_message', 'Failed to deserialize player resources.',
                                             'ad_network', provider,
                                             'external_event_id', externalEventId)
                                           redis.call('SET', callbackKey, rewardIntentId)
                                           redis.call('ZREM', pendingIndexKey, rewardIntentId)
                                           return { 'REWARD_GRANT_FAILED', 'failed', '', 'Failed to deserialize player resources.' }
                                         end
                                       end

                                       local currentGems = tonumber(resources['Gems']) or 0
                                       local nextGems = currentGems + rewardAmount
                                       resources['Gems'] = nextGems
                                       if resources['Gold'] == nil then resources['Gold'] = 0 end
                                       if resources['Energy'] == nil then resources['Energy'] = 0 end

                                       local encodedResources = cjson.encode(resources)
                                       redis.call('SET', resourcesKey, encodedResources)
                                       local currentVersion = tonumber(redis.call('GET', resourcesVersionKey) or '0')
                                       redis.call('SET', resourcesVersionKey, tostring(currentVersion + 1))

                                       redis.call('HSET', intentKey,
                                         'status', 'fulfilled',
                                         'updated_at_utc', tostring(nowMs),
                                         'fulfilled_at_utc', tostring(nowMs),
                                         'new_crystals_balance', tostring(nextGems),
                                         'error_code', '',
                                         'error_message', '',
                                         'ad_network', provider,
                                         'external_event_id', externalEventId)
                                       redis.call('SET', callbackKey, rewardIntentId)
                                       redis.call('ZREM', pendingIndexKey, rewardIntentId)

                                       return { 'OK', 'fulfilled', tostring(nextGems), '' }
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

    public async Task<RewardIntentConfirmResult> ConfirmFromCallbackAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string expectedRewardId,
        int rewardAmount,
        long nowUtcMs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var callbackKey = GetCallbackKey(provider, externalEventId);
        var result = await _database.ScriptEvaluateAsync(
                ConfirmCallbackLua,
                [GetIntentKey(rewardIntentId), callbackKey, PendingByExpiryKey],
                [nowUtcMs, provider, externalEventId, rewardIntentId, expectedRewardId, rewardAmount])
            .WaitAsync(ct);

        RedisResult[] values;
        try
        {
            values = (RedisResult[])result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected callback confirmation script payload type.");
            return ParseConfirmResult(null, null, null, "Unexpected callback processing result.");
        }

        var resultCode = values.Length > 0 ? values[0].ToString() : RewardIntentErrorCodes.RewardGrantFailed;
        var status = values.Length > 1 ? values[1].ToString() : RewardIntentStatuses.Failed;
        var balanceRaw = values.Length > 2 ? values[2].ToString() : null;
        var errorMessage = values.Length > 3 ? values[3].ToString() : null;

        return ParseConfirmResult(resultCode, status, balanceRaw, errorMessage);
    }

    public static RewardIntentConfirmResult ParseConfirmResult(
        string? resultCode,
        string? status,
        string? balanceRaw,
        string? errorMessage)
    {
        return new RewardIntentConfirmResult
        {
            ResultCode = string.IsNullOrWhiteSpace(resultCode) ? RewardIntentErrorCodes.RewardGrantFailed : resultCode!,
            Status = string.IsNullOrWhiteSpace(status) ? RewardIntentStatuses.Failed : status!,
            NewCrystalsBalance = TryParseNullableInt(balanceRaw),
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage
        };
    }

    private static string GetIntentKey(string rewardIntentId) => $"reward:intent:{rewardIntentId}";
    private static string GetCallbackKey(string provider, string externalEventId) => $"reward:callback:{provider}:{externalEventId}";

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
            new HashEntry(NewCrystalsBalanceField, intent.NewCrystalsBalance?.ToString() ?? string.Empty),
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
            NewCrystalsBalance = TryParseNullableInt(GetNullableString(map, NewCrystalsBalanceField)),
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

    private static int? TryParseNullableInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
