using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/battle-pass")]
public sealed class BattlePassController : ControllerBase
{
    private readonly IBattlePassService _battlePassService;
    private readonly IBattlePassAdminService? _battlePassAdminService;

    public BattlePassController(IBattlePassService battlePassService, IBattlePassAdminService? battlePassAdminService = default)
    {
        _battlePassService = battlePassService;
        _battlePassAdminService = battlePassAdminService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<BattlePassCurrentResponse>> GetCurrent([FromQuery] string playerId, CancellationToken ct)
    {
        try
        {
            return Ok(await _battlePassService.GetCurrentAsync(playerId, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("schedule")]
    public async Task<ActionResult<BattlePassScheduleResponse>> GetSchedule(CancellationToken ct)
    {
        return Ok(await _battlePassService.GetScheduleAsync(ct));
    }

    [HttpPost("claim")]
    public async Task<ActionResult<BattlePassClaimResponse>> Claim([FromBody] ClaimBattlePassRewardRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _battlePassService.ClaimAsync(request, ct);
            if (response.Success)
            {
                return Ok(response);
            }

            return response.ErrorCode switch
            {
                BattlePassErrorCodes.SeasonNotFound => NotFound(response),
                _ => BadRequest(response)
            };
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("claim-all")]
    public async Task<ActionResult<BattlePassClaimResponse>> ClaimAll([FromBody] ClaimAllBattlePassRewardsRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _battlePassService.ClaimAllAsync(request, ct);
            if (response.Success)
            {
                return Ok(response);
            }

            return response.ErrorCode switch
            {
                BattlePassErrorCodes.SeasonNotFound => NotFound(response),
                _ => BadRequest(response)
            };
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("xp/add")]
    public async Task<ActionResult<AddBattlePassXpResponse>> AddXp([FromBody] AddBattlePassXpRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new AddBattlePassXpResponse
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.ValidationError,
                ErrorMessage = "Request body is required."
            });
        }

        try
        {
            var state = await _battlePassService.AddXpToCurrentSeasonAsync(request.PlayerId, request.Amount, ct);
            return Ok(new AddBattlePassXpResponse
            {
                Success = true,
                AddedXp = request.Amount,
                BattlePass = state
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AddBattlePassXpResponse
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.ValidationError,
                ErrorMessage = ex.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new AddBattlePassXpResponse
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.PlayerNotFound,
                ErrorMessage = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            var errorCode = string.Equals(ex.Message, "No active battle pass season is available.", StringComparison.Ordinal)
                ? BattlePassErrorCodes.NoActiveSeason
                : BattlePassErrorCodes.SeasonNotActive;

            return StatusCode(StatusCodes.Status409Conflict, new AddBattlePassXpResponse
            {
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = ex.Message
            });
        }
    }
    
    [HttpPost("dev/grant-battle-pass")]
    public async Task<ActionResult<VerifyGooglePurchaseResponse>> Verify([FromBody] VerifyGooglePurchaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(_battlePassAdminService);

        var stubRequest = new AdminGrantEntitlementRequest
        {
            PlayerId = request.PlayerId,
            SeasonId = request.SeasonId,
            PassType = BattlePassPassTypes.Premium
        };

        var response = await _battlePassAdminService.GrantEntitlementAsync(stubRequest, ct);
        if (response.Success)
        {
            return Ok(await BuildSuccessResponseAsync(
                request.PlayerId,
                request.SeasonId,
                response.Entitlement!,
                VerifyGooglePurchaseStatuses.VerifiedAndConsumed,
                ct));
        }

        if (string.Equals(response.ErrorCode, BattlePassErrorCodes.PassAlreadyOwned, StringComparison.Ordinal))
        {
            var battlePass = await _battlePassService.GetSeasonStateAsync(request.PlayerId, request.SeasonId, ct);
            return Ok(BuildDuplicateSuccessResponse(battlePass));
        }

        return ToActionResult(BuildFailureResponse(response));
    }

    private async Task<VerifyGooglePurchaseResponse> BuildSuccessResponseAsync(
        string playerId,
        string seasonId,
        UserEntitlementRecord entitlement,
        string purchaseStatus,
        CancellationToken ct)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = true,
            PurchaseStatus = purchaseStatus,
            GoogleFinalizeStatus = GooglePlayFinalizeStatuses.NotApplicable,
            Entitlement = BuildEntitlementDto(entitlement),
            BattlePass = await _battlePassService.GetSeasonStateAsync(playerId, seasonId, ct)
        };
    }

    private static VerifyGooglePurchaseResponse BuildDuplicateSuccessResponse(BattlePassStateDto battlePass)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = true,
            PurchaseStatus = VerifyGooglePurchaseStatuses.DuplicateAlreadyProcessed,
            GoogleFinalizeStatus = GooglePlayFinalizeStatuses.NotApplicable,
            Entitlement = BuildEntitlementDto(battlePass),
            BattlePass = battlePass
        };
    }

    private static PurchaseEntitlementDto BuildEntitlementDto(UserEntitlementRecord entitlement)
    {
        return new PurchaseEntitlementDto
        {
            Type = entitlement.EntitlementType,
            Key = entitlement.EntitlementKey,
            Status = entitlement.Status
        };
    }

    private static PurchaseEntitlementDto BuildEntitlementDto(BattlePassStateDto battlePass)
    {
        return new PurchaseEntitlementDto
        {
            Type = EntitlementTypes.BattlePass,
            Key = $"battle_pass_{battlePass.PassType}_{battlePass.SeasonId}",
            Status = EntitlementStatuses.Active
        };
    }

    private static VerifyGooglePurchaseResponse BuildFailureResponse(BattlePassAdminOperationResponse response)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = false,
            PurchaseStatus = VerifyGooglePurchaseStatuses.VerifyFailed,
            GoogleFinalizeStatus = GooglePlayFinalizeStatuses.NotApplicable,
            ErrorCode = response.ErrorCode ?? BattlePassErrorCodes.ValidationError,
            ErrorMessage = response.ErrorMessage ?? "Battle pass grant failed."
        };
    }

    private static ActionResult<VerifyGooglePurchaseResponse> ToActionResult(VerifyGooglePurchaseResponse response)
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
