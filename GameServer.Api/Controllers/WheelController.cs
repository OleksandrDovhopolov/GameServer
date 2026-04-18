using GameServer.Api.Models.Dto.Wheel;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

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
            var stopwatch = Stopwatch.StartNew();
            var traceId = HttpContext.TraceIdentifier;
            try
            {
                _logger.LogInformation(
                    "Wheel data request received. TraceId: {TraceId}, CancellationRequested: {CancellationRequested}",
                    traceId,
                    cancellationToken.IsCancellationRequested);
                var result = await _wheelService.GetStateAsync(playerId, cancellationToken);
                _logger.LogInformation(
                    "Wheel data request succeeded. TraceId: {TraceId}, AvailableSpins: {AvailableSpins}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, ElapsedMs: {ElapsedMs}",
                    traceId,
                    result.AvailableSpins,
                    result.UpdatedAt,
                    result.NextUpdateAt,
                    stopwatch.ElapsedMilliseconds);
                return Ok(new WheelDataResponse
                {
                    AvailableSpins = result.AvailableSpins,
                    UpdatedAt = result.UpdatedAt,
                    NextUpdateAt = result.NextUpdateAt
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Wheel data request validation failed. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Wheel data request canceled. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wheel data request failed. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while loading wheel data.");
            }
        }
    }

    [HttpPost("spin")]
    public async Task<ActionResult<WheelSpinResponse>> Spin([FromBody] WheelSpinRequest request, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = request.PlayerId }))
        {
            var stopwatch = Stopwatch.StartNew();
            var traceId = HttpContext.TraceIdentifier;
            try
            {
                _logger.LogInformation(
                    "Wheel spin request received. TraceId: {TraceId}, CancellationRequested: {CancellationRequested}",
                    traceId,
                    cancellationToken.IsCancellationRequested);
                var result = await _wheelService.SpinAsync(request.PlayerId, cancellationToken);
                _logger.LogInformation(
                    "Wheel spin request succeeded. TraceId: {TraceId}, RewardId: {RewardId}, AvailableSpins: {AvailableSpins}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, ElapsedMs: {ElapsedMs}",
                    traceId,
                    result.RewardId,
                    result.AvailableSpins,
                    result.UpdatedAt,
                    result.NextUpdateAt,
                    stopwatch.ElapsedMilliseconds);
                return Ok(new WheelSpinResponse
                {
                    RewardId = result.RewardId,
                    AvailableSpins = result.AvailableSpins,
                    UpdatedAt = result.UpdatedAt,
                    NextUpdateAt = result.NextUpdateAt,
                    RewardGrant = result.RewardGrant
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Wheel spin request validation failed. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (NoSpinsAvailableException ex)
            {
                _logger.LogInformation(
                    "No spins available for player. TraceId: {TraceId}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, ElapsedMs: {ElapsedMs}",
                    traceId,
                    ex.UpdatedAt,
                    ex.NextUpdateAt,
                    stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status403Forbidden, new WheelSpinBlockedResponse
                {
                    Code = "NO_SPINS_AVAILABLE",
                    UpdatedAt = ex.UpdatedAt,
                    NextUpdateAt = ex.NextUpdateAt
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Wheel spin request canceled. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wheel spin request failed. TraceId: {TraceId}, ElapsedMs: {ElapsedMs}", traceId, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while executing wheel spin.");
            }
        }
    }
}
