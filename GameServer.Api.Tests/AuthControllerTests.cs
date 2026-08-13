using GameServer.Api.Controllers;
using GameServer.Api.Models.Domain.Auth;
using GameServer.Api.Models.Dto.Auth;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Anonymous_WhenSuccess_ReturnsTokenResponse()
    {
        var controller = new AuthController(new StubAuthService
        {
            Result = AuthResult.Ok(new IssuedTokens("p1", "access", "refresh", 3600))
        });

        var result = await controller.Anonymous(new AnonymousAuthRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AuthTokenResponse>(ok.Value);
        Assert.Equal("p1", payload.PlayerId);
    }

    [Fact]
    public async Task Refresh_WhenInvalid_ReturnsUnauthorized()
    {
        var controller = new AuthController(new StubAuthService
        {
            Result = AuthResult.Fail(AuthErrorCodes.InvalidRefreshToken, "invalid")
        });

        var result = await controller.Refresh(new RefreshAuthRequest { RefreshToken = "bad" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_WhenProviderUnsupported_ReturnsBadRequest()
    {
        var controller = new AuthController(new StubAuthService
        {
            Result = AuthResult.Fail(AuthErrorCodes.ProviderNotSupported, "unsupported")
        });

        var result = await controller.Login(new ExternalAuthRequest { Provider = "google", Token = "token" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AuthErrorResponse>(badRequest.Value);
        Assert.Equal(AuthErrorCodes.ProviderNotSupported, payload.ErrorCode);
    }

    [Fact]
    public async Task Anonymous_WhenLegacyClaimed_ReturnsConflict()
    {
        var controller = new AuthController(new StubAuthService
        {
            Result = AuthResult.Fail(AuthErrorCodes.LegacyAlreadyClaimed, "claimed")
        });

        var result = await controller.Anonymous(new AnonymousAuthRequest { LegacyPlayerId = "p1" }, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    private sealed class StubAuthService : IAuthService
    {
        public AuthResult Result { get; init; } = AuthResult.Fail(AuthErrorCodes.ValidationError, "failed");

        public Task<AuthResult> AuthenticateAnonymousAsync(string? legacyPlayerId, CancellationToken ct) => Task.FromResult(Result);
        public Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct) => Task.FromResult(Result);
        public Task<AuthResult> LoginAsync(string provider, string token, CancellationToken ct) => Task.FromResult(Result);
        public Task<AuthResult> LinkAsync(string playerId, string provider, string token, CancellationToken ct) => Task.FromResult(Result);
    }
}
