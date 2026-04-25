using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Tests;

internal sealed class FixedClock : ISystemClock
{
    public FixedClock(long utcNowUnixMilliseconds)
    {
        UtcNowUnixMilliseconds = utcNowUnixMilliseconds;
    }

    public long UtcNowUnixMilliseconds { get; }
}

internal sealed class NoOpDistributedLockService : IDistributedLockService
{
    public Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IAsyncDisposable>(new Releaser());
    }

    private sealed class Releaser : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class InMemoryPlayerEconomyRepository : IPlayerEconomyRepository
{
    private readonly HashSet<string> _players = new(StringComparer.Ordinal);

    public void Seed(string playerId) => _players.Add(playerId);

    public Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_players.Contains(playerId));
    }
}

internal sealed class InMemoryBattlePassSeasonRepository : IBattlePassSeasonRepository
{
    private readonly Dictionary<string, BattlePassSeasonRecord> _seasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BattlePassLevelRecord>> _levels = new(StringComparer.Ordinal);

    public Task<BattlePassSeasonRecord?> GetByIdAsync(string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_seasons.TryGetValue(seasonId, out var season) ? season : null);
    }

    public Task<IReadOnlyList<BattlePassSeasonRecord>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BattlePassSeasonRecord>>(_seasons.Values.ToList());
    }

    public Task SaveAsync(BattlePassSeasonRecord season, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _seasons[season.Id] = season;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BattlePassLevelRecord>> GetLevelsAsync(string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BattlePassLevelRecord>>(
            _levels.TryGetValue(seasonId, out var levels)
                ? levels.ToList()
                : []);
    }

    public Task SaveLevelsAsync(string seasonId, IReadOnlyList<BattlePassLevelRecord> levels, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _levels[seasonId] = levels.ToList();
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryBattlePassStateRepository : IBattlePassStateRepository
{
    private readonly Dictionary<string, UserBattlePassRecord> _records = new(StringComparer.Ordinal);

    public Task<UserBattlePassRecord?> GetAsync(string playerId, string seasonId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(BuildKey(playerId, seasonId), out var record) ? record : null);
    }

    public Task<IReadOnlyList<UserBattlePassRecord>> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<UserBattlePassRecord>>(
            _records.Values.Where(x => string.Equals(x.PlayerId, playerId, StringComparison.Ordinal)).ToList());
    }

    public Task SaveAsync(UserBattlePassRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _records[BuildKey(record.PlayerId, record.SeasonId)] = record;
        return Task.CompletedTask;
    }

    private static string BuildKey(string playerId, string seasonId) => $"{playerId}:{seasonId}";
}

internal sealed class InMemoryEntitlementRepository : IEntitlementRepository
{
    private readonly Dictionary<string, UserEntitlementRecord> _records = new(StringComparer.Ordinal);

    public Task<UserEntitlementRecord?> GetByIdAsync(string entitlementId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(entitlementId, out var record) ? record : null);
    }

    public Task<IReadOnlyList<UserEntitlementRecord>> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<UserEntitlementRecord>>(
            _records.Values.Where(x => string.Equals(x.PlayerId, playerId, StringComparison.Ordinal)).ToList());
    }

    public Task SaveAsync(UserEntitlementRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _records[record.Id] = record;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryPurchaseRepository : IPurchaseRepository
{
    private readonly Dictionary<string, PurchaseRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokenIndex = new(StringComparer.Ordinal);

    public Task<PurchaseRecord?> GetByIdAsync(string purchaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(purchaseId, out var record) ? record : null);
    }

    public Task<PurchaseRecord?> GetByTokenHashAsync(string purchaseTokenHash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_tokenIndex.TryGetValue(purchaseTokenHash, out var purchaseId) &&
            _records.TryGetValue(purchaseId, out var record))
        {
            return Task.FromResult<PurchaseRecord?>(record);
        }

        return Task.FromResult<PurchaseRecord?>(null);
    }

    public Task<bool> TryCreateTokenIndexAsync(string purchaseTokenHash, string purchaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_tokenIndex.ContainsKey(purchaseTokenHash))
        {
            return Task.FromResult(false);
        }

        _tokenIndex[purchaseTokenHash] = purchaseId;
        return Task.FromResult(true);
    }

    public Task SaveAsync(PurchaseRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _records[record.Id] = record;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryProductCatalogRepository : IProductCatalogRepository
{
    private readonly Dictionary<string, CatalogProductRecord> _records = new(StringComparer.Ordinal);

    public Task<CatalogProductRecord?> GetByIdAsync(string productId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(productId, out var record) ? record : null);
    }

    public Task SaveAsync(CatalogProductRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _records[record.ProductId] = record;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryRewardDefinitionRepository : IRewardDefinitionRepository
{
    private readonly Dictionary<string, RewardDefinition> _definitions = new(StringComparer.Ordinal);

    public void Seed(RewardDefinition definition)
    {
        _definitions[definition.RewardId] = definition;
    }

    public Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_definitions.TryGetValue(rewardId, out var definition) ? definition : null);
    }
}

internal sealed class StaticLiveOpsScheduleItemProvider : ILiveOpsScheduleItemProvider
{
    private readonly IReadOnlyList<LiveOpsScheduleItem> _items;

    public StaticLiveOpsScheduleItemProvider(IReadOnlyList<LiveOpsScheduleItem>? items = null)
    {
        _items = items ?? [];
    }

    public Task<IReadOnlyList<LiveOpsScheduleItem>> GetItemsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_items);
    }
}

internal sealed class FakeGooglePlayPurchaseVerifier : IGooglePlayPurchaseVerifier
{
    private readonly Dictionary<string, GooglePlayPurchaseVerificationResult> _verifyResults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GooglePlayAcknowledgeResult> _ackResults = new(StringComparer.Ordinal);

    public void SeedVerify(string purchaseToken, GooglePlayPurchaseVerificationResult result)
    {
        _verifyResults[purchaseToken] = result;
    }

    public void SeedAcknowledge(string purchaseToken, GooglePlayAcknowledgeResult result)
    {
        _ackResults[purchaseToken] = result;
    }

    public Task<GooglePlayPurchaseVerificationResult> VerifyAsync(string productId, string purchaseToken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_verifyResults[purchaseToken]);
    }

    public Task<GooglePlayAcknowledgeResult> AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_ackResults.TryGetValue(purchaseToken, out var result)
            ? result
            : new GooglePlayAcknowledgeResult { Success = true, ProviderPayloadJson = "{}" });
    }
}

internal sealed class RecordingRewardApplicationService : IRewardApplicationService
{
    public List<string> GrantedRewardIds { get; } = [];
    public string? FailingRewardId { get; set; }

    public Task<GrantRewardResponse> GrantAsync(GrantRewardCommand command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        GrantedRewardIds.Add(command.RewardId);
        if (string.Equals(command.RewardId, FailingRewardId, StringComparison.Ordinal))
        {
            return Task.FromResult(new GrantRewardResponse
            {
                Success = false,
                RewardId = command.RewardId,
                ErrorCode = RewardErrorCodes.RewardApplicationFailed,
                ErrorMessage = "Simulated failure."
            });
        }

        return Task.FromResult(new GrantRewardResponse
        {
            Success = true,
            RewardId = command.RewardId
        });
    }
}

internal sealed class InMemorySaveRepository : ISaveRepository
{
    private readonly Dictionary<string, SaveRecord> _store = new(StringComparer.Ordinal);

    public void Seed(string playerId, string data)
    {
        _store[playerId] = new SaveRecord
        {
            Data = data,
            LastModified = 1
        };
    }

    public Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store[playerId] = new SaveRecord
        {
            Data = data,
            LastModified = 1
        };
        return Task.CompletedTask;
    }

    public Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.TryGetValue(playerId, out var value) ? value : null);
    }

    public Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<long?>(_store.TryGetValue(playerId, out var value) ? value.LastModified : null);
    }

    public Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.Remove(playerId);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.ContainsKey(playerId));
    }
}

internal sealed class InMemoryPlayerAggregateRepository : IPlayerAggregateRepository
{
    private readonly InMemorySaveRepository _saveRepository;
    private readonly Dictionary<string, Dictionary<string, int>> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _inventory = new(StringComparer.Ordinal);

    public InMemoryPlayerAggregateRepository(InMemorySaveRepository saveRepository)
    {
        _saveRepository = saveRepository;
    }

    public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var save = await _saveRepository.LoadAsync(playerId, ct);
        var root = save is null || string.IsNullOrWhiteSpace(save.Data)
            ? new JsonObject()
            : JsonNode.Parse(save.Data) as JsonObject ?? new JsonObject();

        if (!_resources.TryGetValue(playerId, out var resources))
        {
            resources = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Gold"] = 0,
                ["Energy"] = 0,
                ["Gems"] = 0
            };
            _resources[playerId] = resources;
        }

        if (!_inventory.TryGetValue(playerId, out var inventory))
        {
            inventory = new Dictionary<string, int>(StringComparer.Ordinal);
            _inventory[playerId] = inventory;
        }

        return new PlayerAggregate
        {
            PlayerId = playerId,
            RootDocument = root,
            LastModified = save?.LastModified ?? 1,
            Resources = new Dictionary<string, int>(resources, StringComparer.Ordinal),
            InventoryItems = new Dictionary<string, int>(inventory, StringComparer.Ordinal)
        };
    }

    public async Task SaveAsync(PlayerAggregate player, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _resources[player.PlayerId] = new Dictionary<string, int>(player.Resources, StringComparer.Ordinal);
        _inventory[player.PlayerId] = new Dictionary<string, int>(player.InventoryItems, StringComparer.Ordinal);
        await _saveRepository.SaveAsync(player.PlayerId, player.RootDocument.ToJsonString(), ct);
    }

    public async Task DeleteAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _resources.Remove(playerId);
        _inventory.Remove(playerId);
        await _saveRepository.DeleteAsync(playerId, ct);
    }
}

internal static class BattlePassTestFactory
{
    public static BattlePassSeasonRecord CreateSeason(
        string seasonId,
        long startAtUtcMs,
        long endAtUtcMs,
        string status = BattlePassSeasonStatuses.Active,
        int maxLevel = 2)
    {
        return new BattlePassSeasonRecord
        {
            Id = seasonId,
            Title = seasonId,
            StartAtUtcMs = startAtUtcMs,
            EndAtUtcMs = endAtUtcMs,
            MaxLevel = maxLevel,
            Status = status,
            ConfigVersion = "v1",
            PremiumProductId = $"battle_pass_premium_{seasonId}",
            PlatinumProductId = $"battle_pass_platinum_{seasonId}",
            CreatedAtUtcMs = startAtUtcMs - 1000,
            UpdatedAtUtcMs = startAtUtcMs - 1000
        };
    }

    public static List<BattlePassLevelRecord> CreateLevels(string seasonId)
    {
        return
        [
            new BattlePassLevelRecord
            {
                SeasonId = seasonId,
                Level = 1,
                XpRequired = 0,
                DefaultRewardIds = ["reward_default_1"],
                PremiumRewardIds = ["reward_premium_1"]
            },
            new BattlePassLevelRecord
            {
                SeasonId = seasonId,
                Level = 2,
                XpRequired = 100,
                DefaultRewardIds = ["reward_default_2"],
                PremiumRewardIds = ["reward_premium_2"]
            }
        ];
    }

    public static void SeedRewardDefinitions(InMemoryRewardDefinitionRepository repository)
    {
        foreach (var rewardId in new[]
                 {
                     "reward_default_1",
                     "reward_premium_1",
                     "reward_default_2",
                     "reward_premium_2"
                 })
        {
            repository.Seed(new RewardDefinition
            {
                RewardId = rewardId,
                Source = "battle_pass",
                Parts =
                [
                    new RewardPart
                    {
                        Kind = RewardKind.Resource,
                        TargetId = "Gold",
                        Amount = 10
                    }
                ]
            });
        }
    }
}
