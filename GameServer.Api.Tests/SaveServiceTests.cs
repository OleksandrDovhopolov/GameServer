using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
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
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"coins\":100,\"FortuneWheel\":{\"AvailableSpins\":7,\"LastResetTimestamp\":123}}");
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":200}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        Assert.Equal(200, saved["coins"]!.GetValue<int>());
        Assert.Equal(7, GetFortuneWheel(saved)["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(123, GetFortuneWheel(saved)["LastResetTimestamp"]!.GetValue<long>());
    }

    [Fact]
    public async Task Save_WhenIncomingFortuneWheelMissingTimestamp_MergesTimestampFromExisting()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"FortuneWheel\":{\"AvailableSpins\":7,\"LastResetTimestamp\":321}}");
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":500,\"FortuneWheel\":{\"AvailableSpins\":4}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(500, saved["coins"]!.GetValue<int>());
        Assert.Equal(4, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(321, wheel["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(wheel["availableSpins"]);
        Assert.Null(wheel["lastResetTimestamp"]);
    }

    [Fact]
    public async Task Save_WhenIncomingFortuneWheelMissingTimestampAndNoExisting_InitializesTimestampWithNow()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"coins\":1,\"FortuneWheel\":{\"AvailableSpins\":4}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(1, saved["coins"]!.GetValue<int>());
        Assert.Equal(4, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(now, wheel["LastResetTimestamp"]!.GetValue<long>());
    }

    [Fact]
    public async Task Save_WhenIncomingUsesLegacyNestedWheelKeys_CanonicalizesWheelFields()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        var service = CreateService(repository, now);

        await service.SaveAsync("p1", "{\"FortuneWheel\":{\"AvailableSpins\":6,\"availableSpins\":5,\"lastResetTimestamp\":456}}", CancellationToken.None);

        var saved = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = GetFortuneWheel(saved);
        Assert.Equal(6, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(456, wheel["LastResetTimestamp"]!.GetValue<long>());
        Assert.Null(wheel["availableSpins"]);
        Assert.Null(wheel["lastResetTimestamp"]);
    }

    private static SaveService CreateService(InMemorySaveRepository repository, long now)
    {
        return new SaveService(repository, new FixedClock(now), NullLogger<SaveService>.Instance);
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
}
