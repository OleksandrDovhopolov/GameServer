using GameServer.Api.Models.Dto.Resources;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/resources")]
public sealed class ResourcesController : ControllerBase
{
    private readonly ILogger<ResourcesController> _logger;
    private readonly IResourceAdjustmentService _resourceAdjustmentService;

    public ResourcesController(
        ILogger<ResourcesController> logger,
        IResourceAdjustmentService resourceAdjustmentService)
    {
        _logger = logger;
        _resourceAdjustmentService = resourceAdjustmentService;
    }

    [HttpPost("adjust")]
    public async Task<ActionResult<AdjustResourceResponse>> Adjust([FromBody] AdjustResourceCommand command, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["ResourceId"] = command.ResourceId,
                   ["Delta"] = command.Delta,
                   ["Reason"] = command.Reason
               }))
        {
            var result = await _resourceAdjustmentService.AdjustAsync(command, ct);
            var response = new AdjustResourceResponse
            {
                Success = result.Success,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                Resources = result.Snapshot
            };

            if (result.Success)
            {
                return Ok(response);
            }

            return response.ErrorCode switch
            {
                ResourceAdjustmentErrorCodes.InvalidRequest => BadRequest(response),
                ResourceAdjustmentErrorCodes.ResourceNotSupported => BadRequest(response),
                ResourceAdjustmentErrorCodes.PlayerNotFound => NotFound(response),
                ResourceAdjustmentErrorCodes.InsufficientResources => StatusCode(StatusCodes.Status409Conflict, response),
                _ => StatusCode(StatusCodes.Status500InternalServerError, response)
            };
        }
    }
}
