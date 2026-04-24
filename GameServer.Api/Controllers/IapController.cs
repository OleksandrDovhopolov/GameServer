using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/iap/google")]
public sealed class IapController : ControllerBase
{
    private readonly IIapService _iapService;

    public IapController(IIapService iapService)
    {
        _iapService = iapService;
    }

    [HttpPost("verify")]
    public async Task<ActionResult<VerifyGooglePurchaseResponse>> Verify([FromBody] VerifyGooglePurchaseRequest request, CancellationToken ct)
    {
        var response = await _iapService.VerifyGooglePurchaseAsync(request, ct);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            BattlePassErrorCodes.PlayerNotFound => NotFound(response),
            BattlePassErrorCodes.SeasonNotFound => NotFound(response),
            BattlePassErrorCodes.ProductNotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }
}
