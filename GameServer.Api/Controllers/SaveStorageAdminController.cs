using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/save-storage")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class SaveStorageAdminController : ControllerBase
{
    private readonly ISaveStorageBackfillService _backfillService;

    public SaveStorageAdminController(ISaveStorageBackfillService backfillService)
    {
        _backfillService = backfillService;
    }

    [HttpPost("backfill")]
    public async Task<ActionResult<SaveStorageBackfillResponse>> Backfill(
        [FromQuery] bool overwrite = false,
        CancellationToken ct = default)
    {
        var response = await _backfillService.BackfillAsync(overwrite, ct);
        return response.Success ? Ok(response) : StatusCode(StatusCodes.Status500InternalServerError, response);
    }

    [HttpGet("status")]
    public async Task<ActionResult<SaveStorageStatusResponse>> GetStatus(CancellationToken ct)
    {
        return Ok(await _backfillService.GetStatusAsync(ct));
    }
}
