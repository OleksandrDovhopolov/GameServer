using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto.Resources;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class ResourceAdjustmentServiceTests
{
    [Fact]
    public async Task Adjust_WhenDeltaPositive_AddsResource()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var resourcesRepository = new InMemoryPlayerResourcesRepository();
        resourcesRepository.Seed("p1", gold: 10, energy: 0, gems: 0, version: 5);
        var service = CreateService(saveRepository, resourcesRepository);

        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Gold",
            Delta = 15,
            Reason = "debug_add"
        });

        Assert.True(result.Success);
        Assert.Equal(25, result.Snapshot!.Gold);
    }

    [Fact]
    public async Task Adjust_WhenDeltaNegative_RemovesResource()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var resourcesRepository = new InMemoryPlayerResourcesRepository();
        resourcesRepository.Seed("p1", gold: 50, energy: 0, gems: 0, version: 1);
        var service = CreateService(saveRepository, resourcesRepository);

        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Gold",
            Delta = -20,
            Reason = "spend_action"
        });

        Assert.True(result.Success);
        Assert.Equal(30, result.Snapshot!.Gold);
    }

    [Fact]
    public async Task Adjust_WhenDeltaZero_ReturnsInvalidRequest()
    {
        var service = CreateService(new InMemorySaveRepository(), new InMemoryPlayerResourcesRepository());
        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Gold",
            Delta = 0,
            Reason = "debug_add"
        });

        Assert.False(result.Success);
        Assert.Equal(ResourceAdjustmentErrorCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Adjust_WhenResourceUnsupported_ReturnsResourceNotSupported()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var service = CreateService(saveRepository, new InMemoryPlayerResourcesRepository());

        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Mana",
            Delta = 10,
            Reason = "debug_add"
        });

        Assert.False(result.Success);
        Assert.Equal(ResourceAdjustmentErrorCodes.ResourceNotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task Adjust_WhenPlayerMissing_ReturnsPlayerNotFound()
    {
        var service = CreateService(new InMemorySaveRepository(), new InMemoryPlayerResourcesRepository());

        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "missing",
            ResourceId = "Gold",
            Delta = 10,
            Reason = "debug_add"
        });

        Assert.False(result.Success);
        Assert.Equal(ResourceAdjustmentErrorCodes.PlayerNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Adjust_WhenInsufficientBalance_ReturnsConflictErrorAndPreservesState()
    {
        var saveRepository = new InMemorySaveRepository();
        saveRepository.Seed("p1");
        var resourcesRepository = new InMemoryPlayerResourcesRepository();
        resourcesRepository.Seed("p1", gold: 5, energy: 0, gems: 0, version: 2);
        var service = CreateService(saveRepository, resourcesRepository);

        var result = await service.AdjustAsync(new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Gold",
            Delta = -10,
            Reason = "spend_action"
        });

        Assert.False(result.Success);
        Assert.Equal(ResourceAdjustmentErrorCodes.InsufficientResources, result.ErrorCode);
        var state = await resourcesRepository.GetAsync("p1", CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(5, state!.Resources["Gold"]);
        Assert.Equal(2, state.Version);
    }

    private static ResourceAdjustmentService CreateService(
        InMemorySaveRepository saveRepository,
        InMemoryPlayerResourcesRepository resourcesRepository)
    {
        return new ResourceAdjustmentService(
            saveRepository,
            resourcesRepository,
            NullLogger<ResourceAdjustmentService>.Instance);
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

    private sealed class InMemoryPlayerResourcesRepository : IPlayerResourcesRepository
    {
        private readonly Dictionary<string, PlayerResourcesRecord> _store = new(StringComparer.Ordinal);

        public void Seed(string playerId, int gold, int energy, int gems, long version)
        {
            _store[playerId] = new PlayerResourcesRecord
            {
                PlayerId = playerId,
                Version = version,
                Resources = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Gold"] = gold,
                    ["Energy"] = energy,
                    ["Gems"] = gems
                }
            };
        }

        public Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_store.TryGetValue(playerId, out var value))
            {
                return Task.FromResult<PlayerResourcesRecord?>(null);
            }

            return Task.FromResult<PlayerResourcesRecord?>(Clone(value));
        }

        public Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _store[record.PlayerId] = Clone(record);
            return Task.CompletedTask;
        }

        private static PlayerResourcesRecord Clone(PlayerResourcesRecord source)
        {
            return new PlayerResourcesRecord
            {
                PlayerId = source.PlayerId,
                Version = source.Version,
                Resources = new Dictionary<string, int>(source.Resources, StringComparer.Ordinal)
            };
        }
    }
}
