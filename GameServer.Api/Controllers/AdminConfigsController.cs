using System.Text;
using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/admin/configs")]
[ServiceFilter(typeof(AdminBasicAuthFilter))]
public sealed class AdminConfigsController : ControllerBase
{
    private readonly IConfigService _configService;

    public AdminConfigsController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet("{name}")]
    public async Task<IActionResult> GetConfig(
        [FromRoute] string name,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return BadRequest("'environment' query parameter is required.");

        var result = await _configService.GetAdminConfigAsync(name, environment, cancellationToken);
        return result.Status switch
        {
            ConfigOperationStatus.Success => OkWithEtag(result.Config!),
            ConfigOperationStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException($"Unsupported admin get status: {result.Status}.")
        };
    }

    [HttpPut("{name}")]
    public async Task<IActionResult> Publish(
        [FromRoute] string name,
        [FromQuery] string environment,
        [FromBody] PublishConfigRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return BadRequest("'environment' query parameter is required.");

        var ifMatch = Request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ifMatch))
            return BadRequest("If-Match header is required.");

        var result = await _configService.PublishAsync(
            name,
            environment,
            request.Json,
            ifMatch,
            GetAdminUser(),
            request.Comment,
            cancellationToken);

        return result.Status switch
        {
            ConfigOperationStatus.Success => OkWithEtag(result.Config!),
            ConfigOperationStatus.PreconditionFailed => StatusCode(
                StatusCodes.Status412PreconditionFailed,
                result.ErrorMessage ?? "A newer version exists. Pull the latest, merge, and retry."),
            ConfigOperationStatus.ValidationFailed => BadRequest(result.ErrorMessage),
            _ => throw new InvalidOperationException($"Unsupported publish status: {result.Status}.")
        };
    }

    [HttpGet("{name}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string name,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return BadRequest("'environment' query parameter is required.");

        var history = await _configService.GetHistoryAsync(name, environment, cancellationToken);
        return Ok(history);
    }

    [HttpPost("{name}/rollback")]
    public async Task<IActionResult> Rollback(
        [FromRoute] string name,
        [FromQuery] string environment,
        [FromQuery] long to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return BadRequest("'environment' query parameter is required.");

        var result = await _configService.RollbackAsync(name, environment, to, GetAdminUser(), cancellationToken);
        return result.Status switch
        {
            ConfigOperationStatus.Success => OkWithEtag(result.Config!),
            ConfigOperationStatus.NotFound => NotFound(
                result.ErrorMessage ?? $"Version {to} of '{name}' in environment '{environment}' not found."),
            _ => throw new InvalidOperationException($"Unsupported rollback status: {result.Status}.")
        };
    }

    [HttpPost("{name}/promote")]
    public async Task<IActionResult> Promote(
        [FromRoute] string name,
        [FromQuery] string from,
        [FromQuery] string to,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest("'from' and 'to' query parameters are required.");

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return BadRequest("'from' and 'to' environments must be different.");

        var result = await _configService.PromoteAsync(name, from, to, GetAdminUser(), cancellationToken);
        return result.Status switch
        {
            ConfigOperationStatus.Success => OkWithEtag(result.Config!),
            ConfigOperationStatus.NotFound => NotFound(
                result.ErrorMessage ?? $"Config '{name}' not found in environment '{from}'."),
            _ => throw new InvalidOperationException($"Unsupported promote status: {result.Status}.")
        };
    }

    private string GetAdminUser()
    {
        if (HttpContext.Items.TryGetValue(AdminBasicAuthFilter.AuthenticatedUserItemKey, out var value) &&
            value is string username &&
            !string.IsNullOrWhiteSpace(username))
        {
            return username;
        }

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authHeader["Basic ".Length..].Trim()));
                var colon = decoded.IndexOf(':');
                if (colon > 0)
                    return decoded[..colon];
            }
            catch (FormatException)
            {
            }
        }

        throw new InvalidOperationException("Authenticated admin username is unavailable.");
    }

    private OkObjectResult OkWithEtag(AdminConfigDto dto)
    {
        Response.Headers.ETag = $"\"{dto.Etag}\"";
        return Ok(dto);
    }
}
