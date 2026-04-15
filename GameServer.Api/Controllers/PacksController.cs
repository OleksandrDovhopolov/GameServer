using GameServer.Api.Models.Dto;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/packs")]
public class PackOpenController : ControllerBase
{
    private readonly IPackService _packService;

    public PackOpenController(IPackService packService)
    {
        _packService = packService;
    }

    [HttpPost("open")]
    public async Task<ActionResult<OpenPackResponse>> OpenPack(OpenPackRequest request, CancellationToken ct)
    {
        var result = await _packService.OpenPackAsync(request, ct);
        return Ok(result);
    }
}