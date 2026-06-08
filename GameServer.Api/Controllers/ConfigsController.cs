using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/configs")]
public sealed class ConfigsController : ControllerBase
{
    private const string DefaultPublicEnvironment = "prod";
    private readonly IConfigService _configService;

    public ConfigsController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet("manifest")]
    public async Task<IActionResult> GetManifest(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var manifest = await _configService.GetManifestAsync(
            ResolvePublicEnvironment(environment),
            cancellationToken);
        return Ok(manifest);
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetConfig(
        [FromRoute] string name,
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var result = await _configService.GetConfigAsync(
            name,
            ResolvePublicEnvironment(environment),
            Request.Headers.IfNoneMatch.FirstOrDefault(),
            cancellationToken);

        return result.Status switch
        {
            ConfigOperationStatus.NotModified => StatusCode(StatusCodes.Status304NotModified),
            ConfigOperationStatus.NotFound => NotFound(),
            ConfigOperationStatus.Success => ContentWithEtag(result),
            _ => throw new InvalidOperationException($"Unsupported config status: {result.Status}.")
        };
    }

    private ContentResult ContentWithEtag(ConfigContentResult result)
    {
        Response.Headers.ETag = $"\"{result.Etag}\"";
        return Content(result.Json!, "application/json");
    }

    private static string ResolvePublicEnvironment(string? environment) =>
        string.IsNullOrWhiteSpace(environment) ? DefaultPublicEnvironment : environment;
}
