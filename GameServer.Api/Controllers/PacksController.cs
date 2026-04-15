using GameServer.Api.Models.Dto;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/packs")]
public class PackOpenController : ControllerBase
{
    private readonly ILogger<PackOpenController> _logger;
    private readonly IPackService _packService;

    public PackOpenController(ILogger<PackOpenController> logger, IPackService packService)
    {
        _logger = logger;
        _packService = packService;
    }

    [HttpPost("open")]
    public async Task<ActionResult<OpenPackResponse>> OpenPack(OpenPackRequest request, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = request.PlayerId }))
        {
            _logger.LogInformation("Opening pack {PackId}, {OpenPackRequestId}", request.PackId,  request.OpenPackRequestId);
            var result = await _packService.OpenPackAsync(request, ct);
            _logger.LogInformation("Returned cards: {Cards}", string.Join(", ", result.OpenedCardIds));
            return Ok(result);
        }
    }
}