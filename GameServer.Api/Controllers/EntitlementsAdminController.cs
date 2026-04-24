using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/entitlements")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class EntitlementsAdminController : ControllerBase
{
    private readonly IBattlePassAdminService _battlePassAdminService;

    public EntitlementsAdminController(IBattlePassAdminService battlePassAdminService)
    {
        _battlePassAdminService = battlePassAdminService;
    }

    [HttpPost("grant")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> Grant([FromBody] AdminGrantEntitlementRequest request, CancellationToken ct)
    {
        var response = await _battlePassAdminService.GrantEntitlementAsync(request, ct);
        return ToActionResult(response);
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> Revoke([FromBody] AdminRevokeEntitlementRequest request, CancellationToken ct)
    {
        var response = await _battlePassAdminService.RevokeEntitlementAsync(request, ct);
        return ToActionResult(response);
    }

    private static ActionResult<BattlePassAdminOperationResponse> ToActionResult(BattlePassAdminOperationResponse response)
    {
        if (response.Success)
        {
            return new OkObjectResult(response);
        }

        return response.ErrorCode switch
        {
            BattlePassErrorCodes.PlayerNotFound => new NotFoundObjectResult(response),
            BattlePassErrorCodes.SeasonNotFound => new NotFoundObjectResult(response),
            _ => new BadRequestObjectResult(response)
        };
    }
}
