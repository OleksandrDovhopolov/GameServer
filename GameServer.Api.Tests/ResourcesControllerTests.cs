using GameServer.Api.Controllers;
using GameServer.Api.Models.Domain.Resources;
using GameServer.Api.Models.Dto.Resources;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class ResourcesControllerTests
{
    [Fact]
    public async Task Adjust_WhenSuccess_ReturnsOk()
    {
        var controller = CreateController(new StubService(new AdjustResourceResult
        {
            Success = true,
            Snapshot = new ResourceSnapshotDto { Gold = 10, Energy = 5, Gems = 1 }
        }));

        var result = await controller.Adjust(CreateCommand(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Adjust_WhenInvalidRequest_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService(new AdjustResourceResult
        {
            Success = false,
            ErrorCode = ResourceAdjustmentErrorCodes.InvalidRequest
        }));

        var result = await controller.Adjust(CreateCommand(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Adjust_WhenPlayerNotFound_ReturnsNotFound()
    {
        var controller = CreateController(new StubService(new AdjustResourceResult
        {
            Success = false,
            ErrorCode = ResourceAdjustmentErrorCodes.PlayerNotFound
        }));

        var result = await controller.Adjust(CreateCommand(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Adjust_WhenInsufficientResources_ReturnsConflict()
    {
        var controller = CreateController(new StubService(new AdjustResourceResult
        {
            Success = false,
            ErrorCode = ResourceAdjustmentErrorCodes.InsufficientResources
        }));

        var result = await controller.Adjust(CreateCommand(), CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    private static ResourcesController CreateController(IResourceAdjustmentService service)
    {
        return new ResourcesController(
            NullLogger<ResourcesController>.Instance,
            service);
    }

    private static AdjustResourceCommand CreateCommand()
    {
        return new AdjustResourceCommand
        {
            PlayerId = "p1",
            ResourceId = "Gold",
            Delta = 5,
            Reason = "debug_add"
        };
    }

    private sealed class StubService : IResourceAdjustmentService
    {
        private readonly AdjustResourceResult _result;

        public StubService(AdjustResourceResult result)
        {
            _result = result;
        }

        public Task<AdjustResourceResult> AdjustAsync(AdjustResourceCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
