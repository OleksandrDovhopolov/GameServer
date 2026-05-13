using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Models.Dto.Inventory;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class InventoryManagementServiceTests
{
    [Fact]
    public async Task Add_WhenItemMissing_CreatesItem()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.AddAsync(new AddInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 3,
            Category = "combat",
            Reason = "debug_add"
        });

        Assert.True(result.Success);
        Assert.Equal(3, GetInventoryAmount(result, "bomb"));
    }

    [Fact]
    public async Task Add_WhenItemExists_IncrementsItem()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 2
        }, version: 1);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.AddAsync(new AddInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 4,
            Reason = "debug_add"
        });

        Assert.True(result.Success);
        Assert.Equal(6, GetInventoryAmount(result, "bomb"));
    }

    [Fact]
    public async Task Remove_WhenSufficient_DecrementsItem()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 5
        }, version: 2);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.RemoveAsync(new RemoveInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 2,
            Reason = "spend_action"
        });

        Assert.True(result.Success);
        Assert.Equal(3, GetInventoryAmount(result, "bomb"));
    }

    [Fact]
    public async Task Remove_WhenValueReachesZero_RemovesItem()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 2
        }, version: 2);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.RemoveAsync(new RemoveInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 2,
            Reason = "spend_action"
        });

        Assert.True(result.Success);
        Assert.Empty(result.PlayerState!.InventoryItems);
    }

    [Fact]
    public async Task Remove_WhenInsufficient_ReturnsErrorAndPreservesState()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 1
        }, version: 5);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.RemoveAsync(new RemoveInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 2,
            Reason = "spend_action"
        });

        Assert.False(result.Success);
        Assert.Equal(InventoryErrorCodes.InsufficientInventory, result.ErrorCode);

        var state = await inventoryRepository.GetAsync("p1", CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(1, state!.Items["bomb"]);
        Assert.Equal(5, state.Version);
    }

    [Fact]
    public async Task RemoveBatch_WhenDuplicatesExist_AggregatesAndAppliesAtomically()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 5,
            ["rocket"] = 2
        }, version: 1);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.RemoveBatchAsync(new RemoveInventoryBatchCommand
        {
            PlayerId = "p1",
            Reason = "spend_action",
            Items =
            [
                new InventoryItemDelta { ItemId = "bomb", Amount = 1 },
                new InventoryItemDelta { ItemId = "bomb", Amount = 2 },
                new InventoryItemDelta { ItemId = "rocket", Amount = 1 }
            ]
        });

        Assert.True(result.Success);
        Assert.Equal(2, GetInventoryAmount(result, "bomb"));
        Assert.Equal(1, GetInventoryAmount(result, "rocket"));
    }

    [Fact]
    public async Task RemoveBatch_WhenAnyInsufficient_FailsAndPreservesState()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var inventoryRepository = new InMemoryPlayerInventoryRepository();
        inventoryRepository.Seed("p1", new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 2,
            ["rocket"] = 1
        }, version: 7);
        var aggregateRepository = new InMemoryPlayerAggregateRepository(inventoryRepository);
        var service = CreateService(saveRepository, inventoryRepository, aggregateRepository);

        var result = await service.RemoveBatchAsync(new RemoveInventoryBatchCommand
        {
            PlayerId = "p1",
            Reason = "spend_action",
            Items =
            [
                new InventoryItemDelta { ItemId = "bomb", Amount = 1 },
                new InventoryItemDelta { ItemId = "rocket", Amount = 2 }
            ]
        });

        Assert.False(result.Success);
        Assert.Equal(InventoryErrorCodes.InsufficientInventory, result.ErrorCode);

        var state = await inventoryRepository.GetAsync("p1", CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(2, state!.Items["bomb"]);
        Assert.Equal(1, state.Items["rocket"]);
        Assert.Equal(7, state.Version);
    }

    [Fact]
    public async Task RemoveBatch_WhenInvalidBatch_ReturnsInvalidRequest()
    {
        var service = CreateService(
            new InMemorySaveRepository(),
            new InMemoryPlayerInventoryRepository(),
            new InMemoryPlayerAggregateRepository(new InMemoryPlayerInventoryRepository()));

        var result = await service.RemoveBatchAsync(new RemoveInventoryBatchCommand
        {
            PlayerId = "p1",
            Reason = "spend_action",
            Items = []
        });

        Assert.False(result.Success);
        Assert.Equal(InventoryErrorCodes.InvalidRequest, result.ErrorCode);
    }

    private static InventoryManagementService CreateService(
        InMemorySaveRepository saveRepository,
        InMemoryPlayerInventoryRepository inventoryRepository,
        InMemoryPlayerAggregateRepository aggregateRepository)
    {
        return new InventoryManagementService(
            saveRepository,
            inventoryRepository,
            aggregateRepository,
            NullLogger<InventoryManagementService>.Instance);
    }

    private static int GetInventoryAmount(InventoryOperationResult result, string itemId)
    {
        var item = Assert.Single(result.PlayerState!.InventoryItems.Where(x => x.ItemId == itemId));
        return item.Amount;
    }

    private sealed class InMemorySaveRepository : ISaveRepository
    {
        private readonly HashSet<string> _players = new(StringComparer.Ordinal);

        public void Seed(string playerId)
        {
            _players.Add(playerId);
        }

        public Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _players.Add(playerId);
            return Task.CompletedTask;
        }

        public Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_players.Contains(playerId)
                ? new SaveRecord { Data = "{}", LastModified = 1 }
                : null);
        }

        public Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<long?>(_players.Contains(playerId) ? 1 : null);
        }

        public Task DeleteAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _players.Remove(playerId);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_players.Contains(playerId));
        }
    }

    private sealed class InMemoryPlayerInventoryRepository : IPlayerInventoryRepository
    {
        private readonly Dictionary<string, PlayerInventoryRecord> _store = new(StringComparer.Ordinal);

        public void Seed(string playerId, Dictionary<string, int> items, long version)
        {
            _store[playerId] = new PlayerInventoryRecord
            {
                PlayerId = playerId,
                Version = version,
                Items = new Dictionary<string, int>(items, StringComparer.Ordinal)
            };
        }

        public Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_store.TryGetValue(playerId, out var value))
            {
                return Task.FromResult<PlayerInventoryRecord?>(null);
            }

            return Task.FromResult<PlayerInventoryRecord?>(Clone(value));
        }

        public Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _store[record.PlayerId] = Clone(record);
            return Task.CompletedTask;
        }

        private static PlayerInventoryRecord Clone(PlayerInventoryRecord source)
        {
            return new PlayerInventoryRecord
            {
                PlayerId = source.PlayerId,
                Version = source.Version,
                Items = new Dictionary<string, int>(source.Items, StringComparer.Ordinal)
            };
        }
    }

    private sealed class InMemoryPlayerAggregateRepository : IPlayerAggregateRepository
    {
        private readonly IPlayerInventoryRepository _inventoryRepository;

        public InMemoryPlayerAggregateRepository(IPlayerInventoryRepository inventoryRepository)
        {
            _inventoryRepository = inventoryRepository;
        }

        public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var inventory = await _inventoryRepository.GetAsync(playerId, ct);
            return new PlayerAggregate
            {
                PlayerId = playerId,
                RootDocument = [],
                LastModified = 1,
                Resources = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Gold"] = 0,
                    ["Energy"] = 0,
                    ["Gems"] = 0
                },
                InventoryItems = inventory is null
                    ? new Dictionary<string, int>(StringComparer.Ordinal)
                    : new Dictionary<string, int>(inventory.Items, StringComparer.Ordinal)
            };
        }

        public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
