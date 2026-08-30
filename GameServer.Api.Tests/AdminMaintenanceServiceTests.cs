using GameServer.Api.Configuration;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AdminMaintenanceServiceTests
{
    [Fact]
    public async Task FlushRedisAsync_WhenDisabled_ReturnsFeatureDisabled()
    {
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: false, new FakeRedisAdminClient(), new FakePlayerAggregateRepository());

        var response = await service.FlushRedisAsync(new AdminRedisFlushRequest
        {
            Confirm = "FLUSH_REDIS"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.FeatureDisabled, response.ErrorCode);
    }

    [Fact]
    public async Task FlushRedisAsync_WhenConfirmInvalid_ReturnsValidationError()
    {
        var service = CreateService(allowRedisFlush: true, allowPlayerSaveReset: false, new FakeRedisAdminClient(), new FakePlayerAggregateRepository());

        var response = await service.FlushRedisAsync(new AdminRedisFlushRequest
        {
            Confirm = "WRONG"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.ValidationError, response.ErrorCode);
    }

    [Fact]
    public async Task FlushRedisAsync_WhenRedisThrows_ReturnsRedisFlushFailed()
    {
        var service = CreateService(allowRedisFlush: true, allowPlayerSaveReset: false, new FakeRedisAdminClient
        {
            Exception = new RedisServerException("admin disabled")
        }, new FakePlayerAggregateRepository());

        var response = await service.FlushRedisAsync(new AdminRedisFlushRequest
        {
            Confirm = "FLUSH_REDIS"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.RedisFlushFailed, response.ErrorCode);
    }

    [Fact]
    public async Task FlushRedisAsync_WhenValid_ExecutesFlush()
    {
        var client = new FakeRedisAdminClient();
        var service = CreateService(allowRedisFlush: true, allowPlayerSaveReset: false, client, new FakePlayerAggregateRepository());

        var response = await service.FlushRedisAsync(new AdminRedisFlushRequest
        {
            Confirm = "FLUSH_REDIS"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(client.WasFlushed);
    }

    [Fact]
    public async Task ResetPlayerSaveAsync_WhenDisabled_ReturnsFeatureDisabled()
    {
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: false, new FakeRedisAdminClient(), new FakePlayerAggregateRepository());

        var response = await service.ResetPlayerSaveAsync("p1", new AdminPlayerSaveResetRequest
        {
            Confirm = "RESET_PLAYER_SAVE"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.FeatureDisabled, response.ErrorCode);
    }

    [Fact]
    public async Task ResetPlayerSaveAsync_WhenPlayerIdMissing_ReturnsValidationError()
    {
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: true, new FakeRedisAdminClient(), new FakePlayerAggregateRepository());

        var response = await service.ResetPlayerSaveAsync("", new AdminPlayerSaveResetRequest
        {
            Confirm = "RESET_PLAYER_SAVE"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.ValidationError, response.ErrorCode);
    }

    [Fact]
    public async Task ResetPlayerSaveAsync_WhenConfirmInvalid_ReturnsValidationError()
    {
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: true, new FakeRedisAdminClient(), new FakePlayerAggregateRepository());

        var response = await service.ResetPlayerSaveAsync("p1", new AdminPlayerSaveResetRequest
        {
            Confirm = "WRONG"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.ValidationError, response.ErrorCode);
    }

    [Fact]
    public async Task ResetPlayerSaveAsync_WhenRepositoryThrows_ReturnsResetFailed()
    {
        var repository = new FakePlayerAggregateRepository
        {
            DeleteException = new InvalidOperationException("delete failed")
        };
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: true, new FakeRedisAdminClient(), repository);

        var response = await service.ResetPlayerSaveAsync("p1", new AdminPlayerSaveResetRequest
        {
            Confirm = "RESET_PLAYER_SAVE"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AdminMaintenanceErrorCodes.PlayerSaveResetFailed, response.ErrorCode);
    }

    [Fact]
    public async Task ResetPlayerSaveAsync_WhenValid_DeletesPlayerAggregate()
    {
        var repository = new FakePlayerAggregateRepository();
        var service = CreateService(allowRedisFlush: false, allowPlayerSaveReset: true, new FakeRedisAdminClient(), repository);

        var response = await service.ResetPlayerSaveAsync(" p1 ", new AdminPlayerSaveResetRequest
        {
            Confirm = "RESET_PLAYER_SAVE"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("p1", repository.DeletedPlayerId);
    }

    private static AdminMaintenanceService CreateService(
        bool allowRedisFlush,
        bool allowPlayerSaveReset,
        FakeRedisAdminClient client,
        FakePlayerAggregateRepository playerAggregateRepository)
    {
        return new AdminMaintenanceService(
            Options.Create(new AdminToolsOptions
            {
                AllowRedisFlush = allowRedisFlush,
                AllowPlayerSaveReset = allowPlayerSaveReset
            }),
            client,
            playerAggregateRepository,
            NullLogger<AdminMaintenanceService>.Instance);
    }

    private sealed class FakeRedisAdminClient : IRedisAdminClient
    {
        public Exception? Exception { get; init; }
        public bool WasFlushed { get; private set; }

        public Task FlushDatabaseAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            WasFlushed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlayerAggregateRepository : IPlayerAggregateRepository
    {
        public string? DeletedPlayerId { get; private set; }
        public Exception? DeleteException { get; init; }

        public Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task DeleteAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            DeletedPlayerId = playerId;
            return Task.CompletedTask;
        }
    }
}
