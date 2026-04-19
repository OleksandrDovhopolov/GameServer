using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class SaveServiceTests
{
    [Fact]
    public async Task Save_WhenIncomingOmitsFortuneWheel_PreservesExistingFortuneWheel()
    {
        const long now = 1_700_000_001_000;
        const long existingUpdatedAt = 1_700_000_000_123;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"coins\":100,\"FortuneWheel\":{{\"AvailableSpins\":7,\"UpdatedAt\":{existingUpdatedAt}}}}}");
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":200}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        Assert.Equal(200, saved["coins"]!.GetValue<int>());
        Assert.Equal(7, GetFortuneWheel(saved)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(existingUpdatedAt, GetFortuneWheel(saved)["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task Save_WhenIncomingFortuneWheelMissingUpdatedAt_MergesUpdatedAtFromExisting()
    {
        const long now = 1_700_000_001_000;
        const long existingUpdatedAt = 1_700_000_000_321;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":7,\"UpdatedAt\":{existingUpdatedAt}}}}}");
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":500,\"FortuneWheel\":{\"AvailableSpins\":4}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(500, saved["coins"]!.GetValue<int>());
        Assert.Equal(4, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(existingUpdatedAt, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task Save_WhenIncomingFortuneWheelMissingUpdatedAtAndNoExisting_InitializesUpdatedAtWithNow()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":1,\"FortuneWheel\":{\"AvailableSpins\":4}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(1, saved["coins"]!.GetValue<int>());
        Assert.Equal(4, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task Save_WhenIncomingContainsClientNextUpdateAt_RemovesIt()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":6,\"UpdatedAt\":{now - 1000},\"NextUpdateAt\":{now + 1000}}}}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(6, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now - 1000, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["NextUpdateAt"]);
    }

    [Fact]
    public async Task Save_WhenIncomingContainsLegacyWheelKeys_RemovesLegacyAndUsesCanonicalFields()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"FortuneWheel\":{\"AvailableSpins\":6,\"LastResetTimestamp\":456,\"lastResetTimestamp\":789,\"availableSpins\":5}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(6, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["LastResetTimestamp"]);
        Assert.Null(wheel["lastResetTimestamp"]);
        Assert.Null(wheel["availableSpins"]);
    }

    [Fact]
    public async Task Save_WhenIncomingUpdatedAtIsSeconds_NormalizesToMilliseconds()
    {
        const long now = 1_700_000_000_000;
        var updatedAtMs = now - 30_000;
        var updatedAtSeconds = updatedAtMs / 1000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":6,\"UpdatedAt\":{updatedAtSeconds}}}}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(6, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(updatedAtMs, wheel["UpdatedAt"]!.GetValue<long>());
    }

    private static SaveService CreateService(InMemorySaveRepository repository, long now)
    {
        return new SaveService(repository, new TestRepository(repository), new GameSaveAssembler(), new FixedClock(now), NullLogger<SaveService>.Instance);
    }

    [Fact]
    public async Task Save_WhenClientSendsResourcesAndInventory_IgnoresEconomySectionsAndPreservesStructuredData()
    {
        const long now = 1_700_000_000_000;
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{\"coins\":100}");
        var aggregateRepository = new InMemoryPlayerAggregateRepository();
        aggregateRepository.Seed("p1", "{\"coins\":100}", resources: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Gold"] = 500
        }, inventory: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 3
        });

        var service = new SaveService(
            saveRepository,
            aggregateRepository,
            new GameSaveAssembler(),
            new FixedClock(now),
            NullLogger<SaveService>.Instance);

        await service.SaveAsync(
            "p1",
            "{\"coins\":250,\"Resources\":{\"Gold\":1},\"Inventory\":{\"InventoryItems\":{\"bomb\":999}},\"ServerRewards\":{\"Resources\":{\"Gold\":777}}}",
            CancellationToken.None);

        var aggregate = await aggregateRepository.LoadAsync("p1", CancellationToken.None);
        Assert.Equal(500, aggregate.Resources["Gold"]);
        Assert.Equal(3, aggregate.InventoryItems["bomb"]);
        Assert.Equal(250, aggregate.RootDocument["coins"]!.GetValue<int>());
        Assert.Null(aggregate.RootDocument["Resources"]);
        Assert.Null(aggregate.RootDocument["Inventory"]);
        Assert.Null(aggregate.RootDocument["ServerRewards"]);
    }

    [Fact]
    public async Task Save_WhenClientSendsLegacyInventoryFields_RemovesInventoryFromBlobRoot()
    {
        const long now = 1_700_000_000_000;
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1", "{\"coins\":100}");
        var aggregateRepository = new InMemoryPlayerAggregateRepository();
        aggregateRepository.Seed("p1", "{\"coins\":100}", resources: new Dictionary<string, int>(StringComparer.Ordinal), inventory: new Dictionary<string, int>(StringComparer.Ordinal));

        var service = new SaveService(
            saveRepository,
            aggregateRepository,
            new GameSaveAssembler(),
            new FixedClock(now),
            NullLogger<SaveService>.Instance);

        await service.SaveAsync(
            "p1",
            "{\"coins\":250,\"Inventory\":{\"Owners\":[]}}",
            CancellationToken.None);

        var aggregate = await aggregateRepository.LoadAsync("p1", CancellationToken.None);
        Assert.Equal(250, aggregate.RootDocument["coins"]!.GetValue<int>());
        Assert.Null(aggregate.RootDocument["Inventory"]);
    }

    [Fact]
    public async Task Load_BuildsClientSaveJsonFromStructuredEconomy()
    {
        const long now = 1_700_000_000_000;
        var saveRepository = new InMemorySaveRepository();
        var aggregateRepository = new InMemoryPlayerAggregateRepository();
        aggregateRepository.Seed("p1", "{\"coins\":42}", resources: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Gold"] = 77
        }, inventory: new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bomb"] = 2
        });

        var service = new SaveService(
            saveRepository,
            aggregateRepository,
            new GameSaveAssembler(),
            new FixedClock(now),
            NullLogger<SaveService>.Instance);

        var result = await service.LoadAsync("p1", CancellationToken.None);
        var root = JsonNode.Parse(result.Data)!.AsObject();

        Assert.Equal(42, root["coins"]!.GetValue<int>());
        Assert.Equal(77, root["Resources"]!["Gold"]!.GetValue<int>());
        Assert.Equal(2, root["Inventory"]!["InventoryItems"]!["bomb"]!.GetValue<int>());
        Assert.Null(root["ServerRewards"]);
    }

    private static JsonObject GetFortuneWheel(JsonObject root)
    {
        return root["FortuneWheel"]!.AsObject();
    }

    private sealed class FixedClock : ISystemClock
    {
        public FixedClock(long utcNowUnixMilliseconds)
        {
            UtcNowUnixMilliseconds = utcNowUnixMilliseconds;
        }

        public long UtcNowUnixMilliseconds { get; }
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

        public string? GetData(string playerId)
        {
            return _store.TryGetValue(playerId, out var value) ? value.Data : null;
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

    private sealed class InMemoryPlayerAggregateRepository : IPlayerAggregateRepository
    {
        private readonly Dictionary<string, PlayerAggregate> _store = new(StringComparer.Ordinal);

        public void Seed(string playerId, string rootPayload, Dictionary<string, int> resources, Dictionary<string, int> inventory)
        {
            var root = JsonNode.Parse(rootPayload) as JsonObject ?? new JsonObject();
            _store[playerId] = new PlayerAggregate
            {
                PlayerId = playerId,
                RootDocument = root,
                LastModified = 1,
                Resources = new Dictionary<string, int>(resources, StringComparer.Ordinal),
                InventoryItems = new Dictionary<string, int>(inventory, StringComparer.Ordinal)
            };
        }

        public Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_store.TryGetValue(playerId, out var value))
            {
                return Task.FromResult(Clone(value));
            }

            var created = new PlayerAggregate
            {
                PlayerId = playerId,
                RootDocument = new JsonObject(),
                LastModified = 1,
                Resources = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Gold"] = 0,
                    ["Energy"] = 0,
                    ["Gems"] = 0
                },
                InventoryItems = new Dictionary<string, int>(StringComparer.Ordinal)
            };
            _store[playerId] = Clone(created);
            return Task.FromResult(created);
        }

        public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _store[player.PlayerId] = Clone(player);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _store.Remove(playerId);
            return Task.CompletedTask;
        }

        private static PlayerAggregate Clone(PlayerAggregate source)
        {
            return new PlayerAggregate
            {
                PlayerId = source.PlayerId,
                RootDocument = source.RootDocument.DeepClone().AsObject(),
                LastModified = source.LastModified,
                Resources = new Dictionary<string, int>(source.Resources, StringComparer.Ordinal),
                InventoryItems = new Dictionary<string, int>(source.InventoryItems, StringComparer.Ordinal)
            };
        }
    }
}
