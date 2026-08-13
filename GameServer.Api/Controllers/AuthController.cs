using GameServer.Api.Models.Domain.Auth;
using GameServer.Api.Models.Dto.Auth;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [EnableRateLimiting("auth-anonymous")]
    [HttpPost("anonymous")]
    public async Task<ActionResult> Anonymous([FromBody] AnonymousAuthRequest? request, CancellationToken ct)
    {
        var result = await _authService.AuthenticateAnonymousAsync(request?.LegacyPlayerId, ct);
        return ToActionResult(result);
    }

    [EnableRateLimiting("auth-refresh")]
    [HttpPost("refresh")]
    public async Task<ActionResult> Refresh([FromBody] RefreshAuthRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, ct);
        return ToActionResult(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] ExternalAuthRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request.Provider, request.Token, ct);
        return ToActionResult(result);
    }

    [Authorize]
    [HttpPost("link")]
    public async Task<ActionResult> Link([FromBody] ExternalAuthRequest request, CancellationToken ct)
    {
        var playerId = User.FindFirst("sub")?.Value ?? User.FindFirst("player_id")?.Value ?? string.Empty;
        var result = await _authService.LinkAsync(playerId, request.Provider, request.Token, ct);
        return ToActionResult(result);
    }

    private ActionResult ToActionResult(AuthResult result)
    {
        if (result.Success)
        {
            var tokens = result.Tokens!;
            return Ok(new AuthTokenResponse
            {
                PlayerId = tokens.PlayerId,
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                ExpiresInSeconds = tokens.ExpiresInSeconds
            });
        }

        var response = new AuthErrorResponse
        {
            Success = false,
            ErrorCode = result.ErrorCode ?? AuthErrorCodes.ValidationError,
            ErrorMessage = result.ErrorMessage ?? "Auth request failed."
        };

        return response.ErrorCode switch
        {
            AuthErrorCodes.InvalidRefreshToken => Unauthorized(response),
            AuthErrorCodes.LegacyAlreadyClaimed => Conflict(response),
            AuthErrorCodes.IdentityAlreadyLinked => Conflict(response),
            AuthErrorCodes.AuthNotConfigured => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}
