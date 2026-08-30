using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/test")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class AdminTestController : ControllerBase
{
    private readonly IAdminMaintenanceService _adminMaintenanceService;

    public AdminTestController(IAdminMaintenanceService adminMaintenanceService)
    {
        _adminMaintenanceService = adminMaintenanceService;
    }

    [HttpPost("player/{id}/save/reset")]
    public async Task<ActionResult<AdminMaintenanceOperationResponse>> ResetPlayerSave(
        [FromRoute] string id,
        [FromBody] AdminPlayerSaveResetRequest request,
        CancellationToken ct)
    {
        var response = await _adminMaintenanceService.ResetPlayerSaveAsync(id, request, ct);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            AdminMaintenanceErrorCodes.FeatureDisabled => StatusCode(StatusCodes.Status403Forbidden, response),
            AdminMaintenanceErrorCodes.PlayerSaveResetFailed => StatusCode(StatusCodes.Status500InternalServerError, response),
            _ => BadRequest(response)
        };
    }

    [HttpPost("redis/flush")]
    public async Task<ActionResult<AdminMaintenanceOperationResponse>> FlushRedis(
        [FromBody] AdminRedisFlushRequest request,
        CancellationToken ct)
    {
        var response = await _adminMaintenanceService.FlushRedisAsync(request, ct);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            AdminMaintenanceErrorCodes.FeatureDisabled => StatusCode(StatusCodes.Status403Forbidden, response),
            _ => BadRequest(response)
        };
    }
}
