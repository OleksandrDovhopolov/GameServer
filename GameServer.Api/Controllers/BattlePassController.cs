using GameServer.Api.Models.Dto.BattlePass;
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
}
