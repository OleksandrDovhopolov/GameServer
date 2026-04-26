using System.Text.Json;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisBattlePassSeasonRepository : IBattlePassSeasonRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SeasonIndexKey = "bp:season:index";

    private readonly IDatabase _database;

    public RedisBattlePassSeasonRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<BattlePassSeasonRecord?> GetByIdAsync(string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetSeasonKey(seasonId)).WaitAsync(ct);
        return DeserializeSeason(raw);
    }

    public async Task<IReadOnlyList<BattlePassSeasonRecord>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ids = await _database.SetMembersAsync(SeasonIndexKey).WaitAsync(ct);
        if (ids.Length == 0)
        {
            return [];
        }

        var values = await _database.StringGetAsync(ids.Select(x => (RedisKey)GetSeasonKey(x!)).ToArray()).WaitAsync(ct);
        return values
            .Select(DeserializeSeason)
            .Where(x => x is not null)
            .Cast<BattlePassSeasonRecord>()
            .ToList();
    }

    public async Task SaveAsync(BattlePassSeasonRecord season, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(GetSeasonKey(season.Id), JsonSerializer.Serialize(season, JsonOptions));
        _ = transaction.SetAddAsync(SeasonIndexKey, season.Id);
        _ = await transaction.ExecuteAsync().WaitAsync(ct);
    }

    public async Task<IReadOnlyList<BattlePassLevelRecord>> GetLevelsAsync(string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetLevelsKey(seasonId)).WaitAsync(ct);
        return Deserialize<List<BattlePassLevelRecord>>(raw) ?? [];
    }

    public async Task SaveLevelsAsync(string seasonId, IReadOnlyList<BattlePassLevelRecord> levels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payload = JsonSerializer.Serialize(levels, JsonOptions);
        await _database.StringSetAsync(GetLevelsKey(seasonId), payload).WaitAsync(ct);
    }

    private static string GetSeasonKey(string seasonId) => $"bp:season:{seasonId}";
    private static string GetLevelsKey(string seasonId) => $"bp:levels:{seasonId}";

    private static T? Deserialize<T>(RedisValue raw)
    {
        if (raw.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(raw!, JsonOptions);
    }

    private static BattlePassSeasonRecord? DeserializeSeason(RedisValue raw)
    {
        var season = Deserialize<BattlePassSeasonRecord>(raw);
        if (season is null || !string.IsNullOrWhiteSpace(season.PremiumProductId) || raw.IsNullOrEmpty)
        {
            return season;
        }

        using var document = JsonDocument.Parse((string)raw!);
        if (!document.RootElement.TryGetProperty("GoldProductId", out var legacyGoldProductId) ||
            string.IsNullOrWhiteSpace(legacyGoldProductId.GetString()))
        {
            return season;
        }

        return new BattlePassSeasonRecord
        {
            Id = season.Id,
            Title = season.Title,
            StartAtUtcMs = season.StartAtUtcMs,
            EndAtUtcMs = season.EndAtUtcMs,
            MaxLevel = season.MaxLevel,
            Status = season.Status,
            ConfigVersion = season.ConfigVersion,
            PremiumProductId = legacyGoldProductId.GetString() ?? string.Empty,
            PlatinumProductId = season.PlatinumProductId,
            CreatedAtUtcMs = season.CreatedAtUtcMs,
            UpdatedAtUtcMs = season.UpdatedAtUtcMs
        };
    }
}

public sealed class RedisBattlePassStateRepository : IBattlePassStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabase _database;

    public RedisBattlePassStateRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<UserBattlePassRecord?> GetAsync(string playerId, string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetStateKey(playerId, seasonId)).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<UserBattlePassRecord>(raw!, JsonOptions);
    }

    public async Task<IReadOnlyList<UserBattlePassRecord>> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var seasonIds = await _database.SetMembersAsync(GetPlayerStateIndexKey(playerId)).WaitAsync(ct);
        if (seasonIds.Length == 0)
        {
            return [];
        }

        var values = await _database.StringGetAsync(seasonIds.Select(x => (RedisKey)GetStateKey(playerId, x!)).ToArray()).WaitAsync(ct);
        return values
            .Where(x => !x.IsNullOrEmpty)
            .Select(x => JsonSerializer.Deserialize<UserBattlePassRecord>(x!, JsonOptions))
            .Where(x => x is not null)
            .Cast<UserBattlePassRecord>()
            .ToList();
    }

    public async Task SaveAsync(UserBattlePassRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(GetStateKey(record.PlayerId, record.SeasonId), JsonSerializer.Serialize(record, JsonOptions));
        _ = transaction.SetAddAsync(GetPlayerStateIndexKey(record.PlayerId), record.SeasonId);
        _ = await transaction.ExecuteAsync().WaitAsync(ct);
    }

    private static string GetStateKey(string playerId, string seasonId) => $"bp:user:{playerId}:{seasonId}";
    private static string GetPlayerStateIndexKey(string playerId) => $"bp:user:{playerId}:index";
}
