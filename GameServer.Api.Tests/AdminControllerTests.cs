using System.Text.Json.Nodes;
using GameServer.Api.Controllers;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AdminControllerTests
{
    [Fact]
    public async Task AdjustSpins_UpdatesCanonicalWheelStateAndClampsToMax()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1",
            "{\"availableSpins\":2,\"lastResetTimestamp\":123,\"FortuneWheel\":{\"AvailableSpins\":7,\"UpdatedAt\":1699999999000,\"LastResetTimestamp\":111,\"NextUpdateAt\":999}}");
        var controller = CreateController(repository, now);

        var actionResult = await controller.AdjustSpins("p1", 5, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AdminPlayerSpinsResponse>(ok.Value);
        Assert.Equal("p1", response.PlayerId);
        Assert.Equal(8, response.AvailableSpins);
        Assert.Equal(1_699_999_999_000, response.UpdatedAt);
        Assert.Equal(1_699_999_999_000 + 120_000, response.NextUpdateAt);

        var root = JsonNode.Parse(repository.GetData("p1")!)!.AsObject();
        var wheel = root["FortuneWheel"]!.AsObject();
        Assert.Equal(8, wheel["AvailableSpins"]!.GetValue<int>());
        Assert.Equal(1_699_999_999_000, wheel["UpdatedAt"]!.GetValue<long>());
        Assert.Null(wheel["NextUpdateAt"]);
        Assert.Null(wheel["LastResetTimestamp"]);
        Assert.Null(root["availableSpins"]);
        Assert.Null(root["lastResetTimestamp"]);
    }

    [Fact]
    public async Task AdjustSpins_WhenUpdatedAtInvalid_RepairsUpdatedAtToNow()
    {
        const long now = 1_700_000_000_000;
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"FortuneWheel\":{\"AvailableSpins\":3,\"UpdatedAt\":1700000001000}}");
        var controller = CreateController(repository, now);

        var actionResult = await controller.AdjustSpins("p1", -1, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<AdminPlayerSpinsResponse>(ok.Value);
        Assert.Equal(2, response.AvailableSpins);
        Assert.Equal(now, response.UpdatedAt);
        Assert.Equal(now + 120_000, response.NextUpdateAt);
    }

    [Fact]
    public async Task DeletePlayer_ReturnsNoContentAndDeletesSave()
    {
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"coins\":100}");
        var controller = CreateController(repository, 1_700_000_000_000);

        var result = await controller.DeletePlayer("p1", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(repository.Has("p1"));
    }

    [Fact]
    public async Task GetRawPlayerSave_WhenMissing_ReturnsNotFound()
    {
        var repository = new InMemorySaveRepository();
        var controller = CreateController(repository, 1_700_000_000_000);

        var result = await controller.GetRawPlayerSave("p1", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetRawPlayerSave_WhenExists_ReturnsJsonPayload()
    {
        var repository = new InMemorySaveRepository();
        repository.Seed("p1", "{\"coins\":55}");
        var controller = CreateController(repository, 1_700_000_000_000);

        var result = await controller.GetRawPlayerSave("p1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var node = Assert.IsAssignableFrom<JsonNode>(ok.Value);
        Assert.Equal(55, node!["coins"]!.GetValue<int>());
    }

    private static AdminController CreateController(InMemorySaveRepository repository, long now)
    {
        return new AdminController(
            NullLogger<AdminController>.Instance,
            repository,
            new FixedClock(now));
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

        public bool Has(string playerId) => _store.ContainsKey(playerId);

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
