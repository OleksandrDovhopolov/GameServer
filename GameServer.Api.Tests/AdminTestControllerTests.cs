using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AdminTestControllerTests
{
    [Fact]
    public async Task FlushRedis_WhenFeatureDisabled_ReturnsForbidden()
    {
        var controller = new AdminTestController(new StubAdminMaintenanceService(new AdminMaintenanceOperationResponse
        {
            Success = false,
            ErrorCode = AdminMaintenanceErrorCodes.FeatureDisabled,
            ErrorMessage = "disabled"
        }));

        var result = await controller.FlushRedis(new AdminRedisFlushRequest
        {
            Confirm = "FLUSH_REDIS"
        }, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task FlushRedis_WhenValidationFails_ReturnsBadRequest()
    {
        var controller = new AdminTestController(new StubAdminMaintenanceService(new AdminMaintenanceOperationResponse
        {
            Success = false,
            ErrorCode = AdminMaintenanceErrorCodes.ValidationError,
            ErrorMessage = "bad"
        }));

        var result = await controller.FlushRedis(new AdminRedisFlushRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class StubAdminMaintenanceService : IAdminMaintenanceService
    {
        private readonly AdminMaintenanceOperationResponse _response;

        public StubAdminMaintenanceService(AdminMaintenanceOperationResponse response)
        {
            _response = response;
        }

        public Task<AdminMaintenanceOperationResponse> FlushRedisAsync(AdminRedisFlushRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(_response);
        }
    }
}
