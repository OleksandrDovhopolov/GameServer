using GameServer.Api.Models.Dto.Inventory;
using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly ILogger<InventoryController> _logger;
    private readonly IInventoryManagementService _inventoryManagementService;

    public InventoryController(
        ILogger<InventoryController> logger,
        IInventoryManagementService inventoryManagementService)
    {
        _logger = logger;
        _inventoryManagementService = inventoryManagementService;
    }

    [HttpPost("remove")]
    public async Task<ActionResult<InventoryOperationResponse>> Remove([FromBody] RemoveInventoryItemCommand command, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["ItemId"] = command.ItemId,
                   ["Amount"] = command.Amount,
                   ["Reason"] = command.Reason
               }))
        {
            var result = await _inventoryManagementService.RemoveAsync(command, ct);
            return MapResult(result);
        }
    }

    [HttpPost("remove-batch")]
    public async Task<ActionResult<InventoryOperationResponse>> RemoveBatch([FromBody] RemoveInventoryBatchCommand command, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["ItemsCount"] = command.Items.Count,
                   ["Reason"] = command.Reason
               }))
        {
            var result = await _inventoryManagementService.RemoveBatchAsync(command, ct);
            return MapResult(result);
        }
    }

    private ActionResult<InventoryOperationResponse> MapResult(InventoryOperationResult result)
    {
        var response = new InventoryOperationResponse
        {
            Success = result.Success,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
            PlayerState = result.PlayerState
        };

        if (result.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            InventoryErrorCodes.InvalidRequest => BadRequest(response),
            InventoryErrorCodes.PlayerNotFound => NotFound(response),
            InventoryErrorCodes.InsufficientInventory => StatusCode(StatusCodes.Status409Conflict, response),
            _ => StatusCode(StatusCodes.Status500InternalServerError, response)
        };
    }
}
