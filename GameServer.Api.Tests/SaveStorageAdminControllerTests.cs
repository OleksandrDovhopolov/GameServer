using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class SaveStorageAdminControllerTests
{
    [Fact]
    public async Task Backfill_WhenSuccessful_ReturnsOk()
    {
        var service = new StubBackfillService
        {
            BackfillResponse = new SaveStorageBackfillResponse { Success = true, Scanned = 1, Written = 1 }
        };
        var controller = new SaveStorageAdminController(service);

        var result = await controller.Backfill(overwrite: false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<SaveStorageBackfillResponse>(ok.Value);
        Assert.True(payload.Success);
    }

    [Fact]
    public async Task Backfill_WhenFailed_ReturnsServerError()
    {
        var service = new StubBackfillService
        {
            BackfillResponse = new SaveStorageBackfillResponse { Success = false, Scanned = 1, Failed = 1 }
        };
        var controller = new SaveStorageAdminController(service);

        var result = await controller.Backfill(overwrite: false, CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, error.StatusCode);
    }

    [Fact]
    public async Task GetStatus_ReturnsOk()
    {
        var service = new StubBackfillService
        {
            StatusResponse = new SaveStorageStatusResponse
            {
                Provider = "Redis",
                RedisPlayers = 2,
                PostgresRows = 1
            }
        };
        var controller = new SaveStorageAdminController(service);

        var result = await controller.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<SaveStorageStatusResponse>(ok.Value);
        Assert.Equal("Redis", payload.Provider);
    }

    private sealed class StubBackfillService : ISaveStorageBackfillService
    {
        public SaveStorageBackfillResponse BackfillResponse { get; init; } = new() { Success = true };
        public SaveStorageStatusResponse StatusResponse { get; init; } = new();

        public Task<SaveStorageBackfillResponse> BackfillAsync(bool overwrite, CancellationToken ct = default)
        {
            return Task.FromResult(BackfillResponse);
        }

        public Task<SaveStorageStatusResponse> GetStatusAsync(CancellationToken ct = default)
        {
            return Task.FromResult(StatusResponse);
        }
    }
}
