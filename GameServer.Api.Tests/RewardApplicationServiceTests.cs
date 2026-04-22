using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardApplicationServiceTests
{
    [Fact]
    public async Task GrantAsync_WhenValid_UpdatesPlayerStateAndReturnsSnapshot()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{\"coins\":5}");
        var service = CreateService(
            saveRepository,
            new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_gold"] = new()
                {
                    RewardId = "reward_gold",
                    Source = "ad",
                    Parts =
                    {
                        new RewardPart { Kind = RewardKind.Resource, TargetId = "Gold", Amount = 50 }
                    }
                }
            });

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "ad",
            RewardId = "reward_gold"
        });

        Assert.True(response.Success);
        Assert.Equal(50, response.PlayerState!.Resources["Gold"]);
    }

    [Fact]
    public async Task GrantAsync_WhenPlayerNotFound_ReturnsPlayerNotFoundError()
    {
        var saveRepository = new InMemorySaveRepository();
        var service = CreateService(saveRepository, new Dictionary<string, RewardDefinition>());

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "missing",
            RewardSource = "ad",
            RewardId = "reward_gold"
        });

        Assert.False(response.Success);
        Assert.Equal(RewardErrorCodes.PlayerNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task GrantAsync_WhenRewardNotFound_ReturnsRewardNotFoundError()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{}");
        var service = CreateService(saveRepository, new Dictionary<string, RewardDefinition>());

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "ad",
            RewardId = "missing_reward"
        });

        Assert.False(response.Success);
        Assert.Equal(RewardErrorCodes.RewardNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task GrantAsync_WhenNoHandlerForPart_ReturnsUnsupportedRewardKindError()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{}");
        var service = CreateService(
            saveRepository,
            new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_cosmetic"] = new()
                {
                    RewardId = "reward_cosmetic",
                    Source = "promo",
                    Parts =
                    {
                        new RewardPart { Kind = RewardKind.Cosmetic, TargetId = "skin_1", Amount = 1 }
                    }
                }
            });

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "promo",
            RewardId = "reward_cosmetic"
        });

        Assert.False(response.Success);
        Assert.Equal(RewardErrorCodes.UnsupportedRewardKind, response.ErrorCode);
    }

    [Fact]
    public async Task GrantAsync_WhenDefinitionHasMultipleParts_AppliesAllParts()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{}");
        var service = CreateService(
            saveRepository,
            new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["starter_pack"] = new()
                {
                    RewardId = "starter_pack",
                    Source = "promo",
                    Parts =
                    {
                        new RewardPart { Kind = RewardKind.Resource, TargetId = "Gold", Amount = 100 },
                        new RewardPart { Kind = RewardKind.InventoryItem, TargetId = "bomb", Amount = 3 }
                    }
                }
            });

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "promo",
            RewardId = "starter_pack"
        });

        Assert.True(response.Success);
        Assert.Equal(100, response.PlayerState!.Resources["Gold"]);
        Assert.Contains(response.PlayerState.InventoryItems, x => x.ItemId == "bomb" && x.Amount == 3);
    }

    [Fact]
    public async Task GrantAsync_WhenDefinitionContainsFreeSpin_DoesNotFailWithUnsupportedRewardKind()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{}");
        var service = CreateService(
            saveRepository,
            new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["free_spin_reward"] = new()
                {
                    RewardId = "free_spin_reward",
                    Source = "promo",
                    Parts =
                    {
                        new RewardPart { Kind = RewardKind.FreeSpin, TargetId = "FortuneWheel", Amount = 1 }
                    }
                }
            });

        var response = await service.GrantAsync(new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "promo",
            RewardId = "free_spin_reward"
        });

        Assert.True(response.Success);
        Assert.Null(response.ErrorCode);
    }

    private static RewardApplicationService CreateService(
        InMemorySaveRepository saveRepository,
        Dictionary<string, RewardDefinition> definitions)
    {
        var handlers = new IRewardHandler[]
        {
            new ResourceRewardHandler(),
            new InventoryRewardHandler(),
            new FreeSpinRewardHandler()
        };

        return new RewardApplicationService(
            saveRepository,
            new InMemoryPlayerAggregateRepository(saveRepository),
            new InMemoryRewardDefinitionRepository(definitions),
            new RewardPartApplier(handlers),
            NullLogger<RewardApplicationService>.Instance);
    }

    private sealed class InMemoryPlayerAggregateRepository : IPlayerAggregateRepository
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

    private sealed class InMemoryRewardDefinitionRepository : IRewardDefinitionRepository
    {
        private readonly IReadOnlyDictionary<string, RewardDefinition> _definitions;

        public InMemoryRewardDefinitionRepository(IReadOnlyDictionary<string, RewardDefinition> definitions)
        {
            _definitions = definitions;
        }

        public Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_definitions.TryGetValue(rewardId, out var definition) ? definition : null);
        }
    }

    private sealed class InMemorySaveRepository : ISaveRepository
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
            return Task.FromResult(_store.TryGetValue(playerId, out var saveRecord) ? saveRecord : null);
        }

        public Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<long?>(_store.TryGetValue(playerId, out var saveRecord) ? saveRecord.LastModified : null);
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
}
