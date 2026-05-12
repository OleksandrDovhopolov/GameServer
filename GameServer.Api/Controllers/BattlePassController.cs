using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/battle-pass")]
public sealed class BattlePassController : ControllerBase
{
    private readonly IBattlePassService _battlePassService;

    public BattlePassController(IBattlePassService battlePassService)
    {
        _battlePassService = battlePassService;
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
        return BadRequest("Verify endpoint works, but verification is not implemented yet.");
    }
}
