using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/rewards")]
public sealed class RewardsController : ControllerBase
{
    private readonly ILogger<RewardsController> _logger;
    private readonly IRewardApplicationService _rewardApplicationService;

    public RewardsController(
        ILogger<RewardsController> logger,
        IRewardApplicationService rewardApplicationService)
    {
        _logger = logger;
        _rewardApplicationService = rewardApplicationService;
    }

    [HttpPost("grant")]
    public async Task<ActionResult<GrantRewardResponse>> Grant([FromBody] GrantRewardCommand command, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["RewardId"] = command.RewardId
               }))
        {
            var response = await _rewardApplicationService.GrantAsync(command, ct);
            if (response.Success)
            {
                return Ok(response);
            }

            return response.ErrorCode switch
            {
                RewardErrorCodes.InvalidRequest => BadRequest(response),
                RewardErrorCodes.PlayerNotFound => NotFound(response),
                RewardErrorCodes.RewardNotFound => NotFound(response),
                RewardErrorCodes.UnsupportedRewardKind => StatusCode(StatusCodes.Status500InternalServerError, response),
                _ => StatusCode(StatusCodes.Status500InternalServerError, response)
            };
        }
    }
}
