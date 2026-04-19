using GameServer.Api.Controllers;
using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Models.Dto.Inventory;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class InventoryControllerTests
{
    [Fact]
    public async Task Remove_WhenSuccess_ReturnsOk()
    {
        var controller = CreateController(new StubService(new InventoryOperationResult
        {
            Success = true,
            Snapshot = new InventorySnapshotDto
            {
                Items = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["bomb"] = 2
                }
            }
        }));

        var result = await controller.Remove(CreateRemoveCommand(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Remove_WhenInvalidRequest_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService(new InventoryOperationResult
        {
            Success = false,
            ErrorCode = InventoryErrorCodes.InvalidRequest
        }));

        var result = await controller.Remove(CreateRemoveCommand(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Remove_WhenPlayerNotFound_ReturnsNotFound()
    {
        var controller = CreateController(new StubService(new InventoryOperationResult
        {
            Success = false,
            ErrorCode = InventoryErrorCodes.PlayerNotFound
        }));

        var result = await controller.Remove(CreateRemoveCommand(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Remove_WhenInsufficientInventory_ReturnsConflict()
    {
        var controller = CreateController(new StubService(new InventoryOperationResult
        {
            Success = false,
            ErrorCode = InventoryErrorCodes.InsufficientInventory
        }));

        var result = await controller.Remove(CreateRemoveCommand(), CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    [Fact]
    public async Task RemoveBatch_WhenSuccess_ReturnsOk()
    {
        var controller = CreateController(new StubService(new InventoryOperationResult
        {
            Success = true,
            Snapshot = new InventorySnapshotDto
            {
                Items = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["bomb"] = 1
                }
            }
        }));

        var result = await controller.RemoveBatch(CreateRemoveBatchCommand(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    private static InventoryController CreateController(IInventoryManagementService service)
    {
        return new InventoryController(
            NullLogger<InventoryController>.Instance,
            service);
    }

    private static RemoveInventoryItemCommand CreateRemoveCommand()
    {
        return new RemoveInventoryItemCommand
        {
            PlayerId = "p1",
            ItemId = "bomb",
            Amount = 1,
            Reason = "spend_action"
        };
    }

    private static RemoveInventoryBatchCommand CreateRemoveBatchCommand()
    {
        return new RemoveInventoryBatchCommand
        {
            PlayerId = "p1",
            Reason = "spend_action",
            Items =
            [
                new InventoryItemDelta
                {
                    ItemId = "bomb",
                    Amount = 1
                }
            ]
        };
    }

    private sealed class StubService : IInventoryManagementService
    {
        private readonly InventoryOperationResult _result;

        public StubService(InventoryOperationResult result)
        {
            _result = result;
        }

        public Task<InventoryOperationResult> AddAsync(AddInventoryItemCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }

        public Task<InventoryOperationResult> RemoveAsync(RemoveInventoryItemCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }

        public Task<InventoryOperationResult> RemoveBatchAsync(RemoveInventoryBatchCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
