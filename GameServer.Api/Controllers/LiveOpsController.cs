using GameServer.Api.Models.Dto.LiveOps;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/liveops")]
public sealed class LiveOpsController : ControllerBase
{
    private readonly ILogger<LiveOpsController> _logger;
    private readonly ILiveOpsScheduleService _liveOpsScheduleService;

    public LiveOpsController(ILogger<LiveOpsController> logger, ILiveOpsScheduleService liveOpsScheduleService)
    {
        _logger = logger;
        _liveOpsScheduleService = liveOpsScheduleService;
    }

    [HttpGet("schedule")]
    public async Task<ActionResult<IReadOnlyList<LiveOpsScheduleItemDto>>> GetSchedule(CancellationToken ct)
    {
        try
        {
            return Ok(await _liveOpsScheduleService.GetScheduleAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to return liveops schedule.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while loading liveops schedule.");
        }
    }
}
