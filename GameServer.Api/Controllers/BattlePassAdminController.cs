using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/battle-pass")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class BattlePassAdminController : ControllerBase
{
    private readonly IBattlePassAdminService _battlePassAdminService;

    public BattlePassAdminController(IBattlePassAdminService battlePassAdminService)
    {
        _battlePassAdminService = battlePassAdminService;
    }

    [HttpPost("seasons")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> CreateSeason([FromBody] CreateBattlePassSeasonRequest request, CancellationToken ct)
    {
        var response = await _battlePassAdminService.CreateSeasonAsync(request, ct);
        return ToActionResult(response);
    }

    [HttpPut("seasons/{seasonId}")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> UpdateSeason(
        [FromRoute] string seasonId,
        [FromBody] UpdateBattlePassSeasonRequest request,
        CancellationToken ct)
    {
        var response = await _battlePassAdminService.UpdateSeasonAsync(seasonId, request, ct);
        return ToActionResult(response);
    }

    [HttpPut("seasons/{seasonId}/levels")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> UpsertLevels(
        [FromRoute] string seasonId,
        [FromBody] UpsertBattlePassLevelsRequest request,
        CancellationToken ct)
    {
        var response = await _battlePassAdminService.UpsertLevelsAsync(seasonId, request, ct);
        return ToActionResult(response);
    }

    [HttpPost("seasons/{seasonId}/publish")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> PublishSeason([FromRoute] string seasonId, CancellationToken ct)
    {
        var response = await _battlePassAdminService.PublishSeasonAsync(seasonId, ct);
        return ToActionResult(response);
    }

    [HttpPost("seasons/{seasonId}/archive")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> ArchiveSeason([FromRoute] string seasonId, CancellationToken ct)
    {
        var response = await _battlePassAdminService.ArchiveSeasonAsync(seasonId, ct);
        return ToActionResult(response);
    }

    [HttpPost("recreate")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> Recreate(
        [FromBody] RecreateBattlePassSeasonRequest request,
        CancellationToken ct)
    {
        var response = await _battlePassAdminService.RecreateSeasonAsync(request, ct);
        return ToActionResult(response);
    }

    [HttpGet("player/{playerId}")]
    public async Task<ActionResult<BattlePassAdminOperationResponse>> GetPlayerState(
        [FromRoute] string playerId,
        [FromQuery] string? seasonId,
        CancellationToken ct)
    {
        var response = await _battlePassAdminService.GetPlayerStateAsync(playerId, seasonId, ct);
        return ToActionResult(response);
    }

    private ActionResult<BattlePassAdminOperationResponse> ToActionResult(BattlePassAdminOperationResponse response)
    {
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            BattlePassErrorCodes.PlayerNotFound => NotFound(response),
            BattlePassErrorCodes.SeasonNotFound => NotFound(response),
            BattlePassErrorCodes.PurchaseNotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }
}
