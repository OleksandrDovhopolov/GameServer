using GameServer.Api.Models.Dto.Wheel;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/wheel")]
public sealed class WheelController : ControllerBase
{
    private readonly ILogger<WheelController> _logger;
    private readonly IWheelService _wheelService;

    public WheelController(ILogger<WheelController> logger, IWheelService wheelService)
    {
        _logger = logger;
        _wheelService = wheelService;
    }

    [HttpGet("rewards")]
    public ActionResult<WheelRewardsResponse> GetRewards()
    {
        try
        {
            var rewards = _wheelService.GetRewards();
            return Ok(new WheelRewardsResponse
            {
                Rewards = rewards.Select(reward => new WheelRewardDto
                {
                    Id = reward.Id,
                    Weight = reward.Weight
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to return wheel rewards.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while loading wheel rewards.");
        }
    }

    [HttpGet("data")]
    public async Task<ActionResult<WheelDataResponse>> GetData([FromQuery] string playerId, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = playerId }))
        {
            try
            {
                var result = await _wheelService.GetStateAsync(playerId, cancellationToken);
                return Ok(new WheelDataResponse
                {
                    AvailableSpins = result.AvailableSpins,
                    NextRegenSeconds = result.NextRegenSeconds
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Wheel data request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Wheel data request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wheel data request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while loading wheel data.");
            }
        }
    }

    [HttpPost("spin")]
    public async Task<ActionResult<WheelSpinResponse>> Spin([FromBody] WheelSpinRequest request, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = request.PlayerId }))
        {
            try
            {
                var result = await _wheelService.SpinAsync(request.PlayerId, cancellationToken);
                return Ok(new WheelSpinResponse
                {
                    RewardId = result.RewardId,
                    AvailableSpins = result.AvailableSpins,
                    NextRegenSeconds = result.NextRegenSeconds
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Wheel spin request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (NoSpinsAvailableException ex)
            {
                _logger.LogInformation("No spins available for player.");
                return StatusCode(StatusCodes.Status403Forbidden, new WheelSpinBlockedResponse
                {
                    Code = "NO_SPINS_AVAILABLE",
                    NextRegenSeconds = ex.NextRegenSeconds
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Wheel spin request canceled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wheel spin request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while executing wheel spin.");
            }
        }
    }
}
