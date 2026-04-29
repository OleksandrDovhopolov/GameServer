using GameServer.Api.Configuration;
using GameServer.Api.Models.Dto.Admin;
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
        var service = CreateService(allowRedisFlush: false, new FakeRedisAdminClient());

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
        var service = CreateService(allowRedisFlush: true, new FakeRedisAdminClient());

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
        var service = CreateService(allowRedisFlush: true, new FakeRedisAdminClient
        {
            Exception = new RedisServerException("admin disabled")
        });

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
        var service = CreateService(allowRedisFlush: true, client);

        var response = await service.FlushRedisAsync(new AdminRedisFlushRequest
        {
            Confirm = "FLUSH_REDIS"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(client.WasFlushed);
    }

    private static AdminMaintenanceService CreateService(bool allowRedisFlush, FakeRedisAdminClient client)
    {
        return new AdminMaintenanceService(
            Options.Create(new AdminToolsOptions
            {
                AllowRedisFlush = allowRedisFlush
            }),
            client,
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
}
