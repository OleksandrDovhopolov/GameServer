using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/purchases")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class PurchasesAdminController : ControllerBase
{
    private readonly IBattlePassAdminService _battlePassAdminService;

    public PurchasesAdminController(IBattlePassAdminService battlePassAdminService)
    {
        _battlePassAdminService = battlePassAdminService;
    }

    [HttpGet("{purchaseId}")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> GetPurchase([FromRoute] string purchaseId, CancellationToken ct)
    {
        var response = await _battlePassAdminService.GetPurchaseAsync(purchaseId, ct);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            BattlePassErrorCodes.PurchaseNotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }

    [HttpPost("{purchaseId}/retry-finalization")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> RetryFinalization(
        [FromRoute] string purchaseId,
        [FromBody] AdminRetryPurchaseFinalizationRequest request,
        CancellationToken ct)
    {
        var response = await _battlePassAdminService.RetryPurchaseFinalizationAsync(purchaseId, request, ct);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            BattlePassErrorCodes.PurchaseNotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }
}
