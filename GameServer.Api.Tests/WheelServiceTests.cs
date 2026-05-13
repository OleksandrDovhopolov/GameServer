using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Collections.Concurrent;

namespace GameServer.Api.Tests;

public sealed class WheelServiceTests
{
    private const long RegenIntervalMs = 2 * 60 * 1000;

    [Fact]
    public async Task GetState_WhenDataDoesNotExist_ReturnsConfiguredDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(8, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["NextUpdateAt"]);
        Assert.Null(wheel["LastResetTimestamp"]);
        Assert.Null(wheel["lastResetTimestamp"]);
        Assert.Null(wheel["availableSpins"]);
    }

    [Fact]
    public async Task GetState_WhenStateExists_ReturnsStoredSpinsAndTimestamps()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(2, updatedAt));
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(2, result.AvailableSpins);
        Assert.Equal(updatedAt, result.UpdatedAt);
        Assert.Equal(updatedAt + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);
    }

    [Fact]
    public async Task Spin_WhenLessThanRegenInterval_DoesNotResetSpins()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":2,\"UpdatedAt\":{updatedAt},\"NextUpdateAt\":1}}}}");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(1, result.AvailableSpins);
        Assert.Equal(updatedAt, result.UpdatedAt);
        Assert.Equal(updatedAt + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(1, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(updatedAt, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["NextUpdateAt"]);
        Assert.Null(wheel["LastResetTimestamp"]);
    }

    [Fact]
    public async Task Spin_WhenExactlyAtRegenInterval_ResetsThenConsumesOneSpin()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - RegenIntervalMs;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(0, updatedAt));
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);
    }

    [Fact]
    public async Task Spin_WhenPayloadIsInvalid_RepairsPayloadAndPersistsDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "not-json");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(7, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task Spin_PreservesUnrelatedRootFields()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"coins\":123}");
        var service = CreateService(repository, now);

        await service.SpinAsync("p1", CancellationToken.None);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        Assert.Equal(123, root["coins"]!.GetValue<int>());
        Assert.Equal(7, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
    }

    [Fact]
    public async Task Spin_WhenNoSpinsAvailable_ThrowsAndPersistsState()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(0, updatedAt));
        var service = CreateService(repository, now);

        var exception = await Assert.ThrowsAsync<NoSpinsAvailableException>(() => service.SpinAsync("p1", CancellationToken.None));

        Assert.Equal(updatedAt, exception.UpdatedAt);
        Assert.Equal(updatedAt + RegenIntervalMs, exception.NextUpdateAt);
        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(0, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(updatedAt, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task GetState_WhenStoredSpinsAreFull_ReturnsFutureNextUpdateTimestamp()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(8, updatedAt));
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(updatedAt, result.UpdatedAt);
        Assert.Equal(updatedAt + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);
    }

    [Fact]
    public async Task GetState_WhenPayloadIsInvalid_RepairsPayloadAndPersistsDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "not-json");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(8, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["NextUpdateAt"]);
    }

    [Fact]
    public async Task GetState_WhenUpdatedAtIsSeconds_ConvertsAndPersistsWithoutReset()
    {
        const long now = 1_700_000_000_000;
        var updatedAtMs = now - (60 * 1000);
        var updatedAtSeconds = updatedAtMs / 1000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":4,\"UpdatedAt\":{updatedAtSeconds}}}}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(4, result.AvailableSpins);
        Assert.Equal(updatedAtMs, result.UpdatedAt);
        Assert.Equal(updatedAtMs + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        Assert.Equal(updatedAtMs, GetFortuneWheel(root)["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task Spin_WhenUpdatedAtIsSeconds_ConvertsAndPersistsWithoutReset()
    {
        const long now = 1_700_000_000_000;
        var updatedAtMs = now - (60 * 1000);
        var updatedAtSeconds = updatedAtMs / 1000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":2,\"UpdatedAt\":{updatedAtSeconds}}}}}");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(1, result.AvailableSpins);
        Assert.Equal(updatedAtMs, result.UpdatedAt);
        Assert.Equal(updatedAtMs + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(1, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(updatedAtMs, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task GetState_WhenUpdatedAtIsFuture_FallsBackToDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":4,\"UpdatedAt\":{now + 1_000}}}}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);
    }

    [Fact]
    public async Task GetState_WhenFortuneWheelMissingUpdatedAt_RepairsWithNowAndKeepsSpins()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"FortuneWheel\":{\"AvailableSpins\":7}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(7, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
    }

    [Fact]
    public async Task GetState_WhenFortuneWheelHasLegacyLastResetTimestamp_IgnoresLegacyAndRepairsUpdatedAt()
    {
        const long now = 1_700_000_000_000;
        var legacyTimestamp = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":6,\"LastResetTimestamp\":{legacyTimestamp}}}}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(6, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(now, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["LastResetTimestamp"]);
    }

    [Fact]
    public async Task GetState_WhenOnlyLegacyRootWheelFieldsExist_InitializesDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"availableSpins\":3,\"lastResetTimestamp\":1700000000}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + RegenIntervalMs, result.NextUpdateAt);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    private static string CreateFortuneWheelPayload(int availableSpins, long updatedAt)
    {
        return $"{{\"FortuneWheel\":{{\"AvailableSpins\":{availableSpins},\"UpdatedAt\":{updatedAt}}}}}";
    }

    [Fact]
    public async Task Spin_WhenAdSpinAvailableAndNoRegularSpins_ConsumesAdSpinWithoutChangingRegularSpins()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":0,\"UpdatedAt\":{updatedAt},\"AdSpinAvailable\":true}}}}");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(0, result.AvailableSpins);
        Assert.False(result.AdSpinAvailable);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(root);
        Assert.Equal(0, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.False(wheel["AdSpinAvailable"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Spin_WhenRegularAndAdSpinAvailable_UsesAdSpinFirst()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":2,\"UpdatedAt\":{updatedAt},\"AdSpinAvailable\":true}}}}");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(2, result.AvailableSpins);
        Assert.False(result.AdSpinAvailable);
    }

    [Fact]
    public async Task Spin_WhenParallelRequestsWithSingleAdSpin_OnlyOneRequestSucceeds()
    {
        const long now = 1_700_000_000_000;
        var updatedAt = now - (60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":0,\"UpdatedAt\":{updatedAt},\"AdSpinAvailable\":true}}}}");
        var service = CreateService(repository, now, new SemaphoreDistributedLockService());

        var firstTask = service.SpinAsync("p1", CancellationToken.None);
        var secondTask = service.SpinAsync("p1", CancellationToken.None);

        var firstResult = await firstTask;
        var secondException = await Assert.ThrowsAsync<NoSpinsAvailableException>(() => secondTask);

        Assert.Equal(0, firstResult.AvailableSpins);
        Assert.False(firstResult.AdSpinAvailable);
        Assert.Equal(updatedAt, secondException.UpdatedAt);
    }

    private static JsonObject GetFortuneWheel(JsonObject root)
    {
        return root["FortuneWheel"]!.AsObject();
    }

    private static WheelService CreateService(
        InMemorySaveRepository repository,
        long now,
        IDistributedLockService? distributedLockService = null)
    {
        return new WheelService(
            new InMemoryPlayerAggregateRepository(repository),
            new FixedWheelRewardCatalog("gold_500"),
            new FixedClock(now),
            new StubRewardDefinitionRepository("wheel"),
            new NoOpRewardPartApplier(),
            distributedLockService ?? new NoOpDistributedLockService(),
            NullLogger<WheelService>.Instance);
    }

    private sealed class FixedWheelRewardCatalog : IWheelRewardCatalog
    {
        private readonly string _rewardId;

        public FixedWheelRewardCatalog(string rewardId)
        {
            _rewardId = rewardId;
        }

        public IReadOnlyList<WheelReward> GetRewards() =>
            [new WheelReward { Id = _rewardId, Weight = 1 }];

        public string RollRewardId() => _rewardId;
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
        private readonly ConcurrentDictionary<string, SaveRecord> _store = new(StringComparer.Ordinal);

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
            _store.TryRemove(playerId, out _);
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
        private readonly InMemorySaveRepository _saveRepository;

        public InMemoryPlayerAggregateRepository(InMemorySaveRepository saveRepository)
        {
            _saveRepository = saveRepository;
        }

        public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var save = await _saveRepository.LoadAsync(playerId, ct);
            JsonObject root = [];
            if (save is not null && !string.IsNullOrWhiteSpace(save.Data))
            {
                try
                {
                    root = JsonNode.Parse(save.Data) as JsonObject ?? [];
                }
                catch
                {
                    root = [];
                }
            }

            return new PlayerAggregate
            {
                PlayerId = playerId,
                RootDocument = root,
                LastModified = save?.LastModified ?? 1
            };
        }

        public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _saveRepository.SaveAsync(player.PlayerId, player.RootDocument.ToJsonString(), ct);
        }

        public Task DeleteAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return _saveRepository.DeleteAsync(playerId, ct);
        }
    }

    private sealed class StubRewardDefinitionRepository : IRewardDefinitionRepository
    {
        private readonly string _source;

        public StubRewardDefinitionRepository(string source)
        {
            _source = source;
        }

        public Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<RewardDefinition?>(new RewardDefinition
            {
                RewardId = rewardId,
                Source = _source,
                Parts = []
            });
        }
    }

    private sealed class NoOpRewardPartApplier : IRewardPartApplier
    {
        public Task ApplyAsync(PlayerAggregate player, RewardDefinition rewardDefinition, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpDistributedLockService : IDistributedLockService
    {
        public Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
        {
            return Task.FromResult<IAsyncDisposable>(new Releaser());
        }

        private sealed class Releaser : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class SemaphoreDistributedLockService : IDistributedLockService
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            return new Releaser(_semaphore);
        }

        private sealed class Releaser : IAsyncDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private int _disposed;

            public Releaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _semaphore.Release();
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
