using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class WheelServiceTests
{
    [Fact]
    public async Task GetState_WhenDataDoesNotExist_ReturnsConfiguredDefaults()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(8, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task GetState_WhenStateExists_ReturnsStoredSpinsAndTimestamps()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (14 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(2, lastReset));
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(2, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, result.NextUpdateAt);
    }

    [Fact]
    public async Task Spin_WhenLessThanRegenInterval_DoesNotResetSpins()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (14 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(2, lastReset));
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(1, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(1, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(lastReset, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task Spin_WhenExactlyAtRegenInterval_ResetsThenConsumesOneSpin()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (15 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(0, lastReset));
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + 15 * 60 * 1000, result.NextUpdateAt);
    }

    [Fact]
    public async Task Spin_WhenGreaterThanRegenInterval_ResetsThenConsumesOneSpin()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (16 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(3, lastReset));
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(now, result.UpdatedAt);
        Assert.Equal(now + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(now, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
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
        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(7, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task Spin_PreservesUnrelatedRootFields()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"coins\":123}");
        var service = CreateService(repository, now);

        await service.SpinAsync("p1", CancellationToken.None);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(123, root["coins"]!.GetValue<int>());
        Assert.Equal(7, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task Spin_WhenNoSpinsAvailable_ThrowsAndPersistsState()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (14 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(0, lastReset));
        var service = CreateService(repository, now);

        var exception = await Assert.ThrowsAsync<NoSpinsAvailableException>(() => service.SpinAsync("p1", CancellationToken.None));

        Assert.Equal(lastReset, exception.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, exception.NextUpdateAt);
        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(0, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(lastReset, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task GetState_WhenStoredSpinsAreFull_ReturnsFutureNextUpdateTimestamp()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (5 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", CreateFortuneWheelPayload(8, lastReset));
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(8, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, result.NextUpdateAt);
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
        Assert.Equal(now + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(8, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task Spin_WhenLegacyRootWheelFieldsExist_RemovesLegacyFieldsAndUsesFortuneWheel()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (5 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":8,\"LastResetTimestamp\":{lastReset}}},\"availableSpins\":1,\"lastResetTimestamp\":1}}");
        var service = CreateService(repository, now);

        var result = await service.SpinAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(7, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(lastReset, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task GetState_WhenLegacyRootWheelFieldsExist_RemovesLegacyFieldsAndUsesFortuneWheel()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (5 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"FortuneWheel\":{{\"AvailableSpins\":7,\"LastResetTimestamp\":{lastReset}}},\"availableSpins\":8,\"lastResetTimestamp\":1}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(7, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(7, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(lastReset, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task GetState_WhenOnlyLegacyRootWheelFieldsExist_MigratesToFortuneWheelWithoutReset()
    {
        const long now = 1_700_000_000_000;
        var lastReset = now - (5 * 60 * 1000);
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", $"{{\"availableSpins\":3,\"lastResetTimestamp\":{lastReset}}}");
        var service = CreateService(repository, now);

        var result = await service.GetStateAsync("p1", CancellationToken.None);

        Assert.Equal(3, result.AvailableSpins);
        Assert.Equal(lastReset, result.UpdatedAt);
        Assert.Equal(lastReset + 15 * 60 * 1000, result.NextUpdateAt);

        var saved = repository.GetData("p1");
        var root = JsonNode.Parse(saved!)!.AsObject();
        Assert.Equal(3, GetFortuneWheel(root)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(lastReset, GetFortuneWheel(root)["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    private static string CreateFortuneWheelPayload(int availableSpins, long lastResetTimestamp)
    {
        return $"{{\"FortuneWheel\":{{\"AvailableSpins\":{availableSpins},\"LastResetTimestamp\":{lastResetTimestamp}}}}}";
    }

    private static JsonObject GetFortuneWheel(JsonObject root)
    {
        return root["FortuneWheel"]!.AsObject();
    }

    private static WheelService CreateService(InMemorySaveRepository repository, long now)
    {
        return new WheelService(
            repository,
            new FixedWheelRewardCatalog("gold_500"),
            new FixedClock(now));
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
}
