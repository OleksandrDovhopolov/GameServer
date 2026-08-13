using System.Security.Claims;
using GameServer.Api.Configuration;
using GameServer.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class PlayerScopeFilterTests
{
    [Fact]
    public async Task Required_ProtectedRouteWithoutBearer_ReturnsUnauthorized()
    {
        var context = CreateContext("api/v1/save/global", "GET", new Dictionary<string, object?> { ["playerId"] = "p1" });
        var executed = await ExecuteAsync(context, AuthMode.Required);

        Assert.False(executed);
        Assert.IsType<UnauthorizedResult>(context.Result);
    }

    [Fact]
    public async Task Optional_ProtectedRouteWithoutBearer_AllowsRequest()
    {
        var context = CreateContext("api/v1/save/global", "GET", new Dictionary<string, object?> { ["playerId"] = "p1" });
        var executed = await ExecuteAsync(context, AuthMode.Optional);

        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Optional_WithBearerAndMatchingQueryPlayer_AllowsRequest()
    {
        var context = CreateContext("api/v1/save/global", "GET", new Dictionary<string, object?> { ["playerId"] = "p1" });
        AddBearer(context, "p1");

        var executed = await ExecuteAsync(context, AuthMode.Optional);

        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Required_WithBearerAndMismatchedDtoPlayer_ReturnsForbidden()
    {
        var context = CreateContext("api/v1/resources/adjust", "POST", new Dictionary<string, object?>
        {
            ["command"] = new PlayerCommand { PlayerId = "other" }
        });
        AddBearer(context, "p1");

        var executed = await ExecuteAsync(context, AuthMode.Required);

        Assert.False(executed);
        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public async Task Required_PublicRewardCallback_AllowsWithoutBearer()
    {
        var context = CreateContext("api/v1/rewards/intent/callback", "GET", new Dictionary<string, object?>());

        var executed = await ExecuteAsync(context, AuthMode.Required);

        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Required_PublicRewardStatus_AllowsWithoutBearer()
    {
        var context = CreateContext("api/v1/rewards/intent/status", "GET", new Dictionary<string, object?>());

        var executed = await ExecuteAsync(context, AuthMode.Required);

        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Required_ProtectedRouteWithoutPlayerId_AllowsAuthenticatedRequest()
    {
        var context = CreateContext("api/v1/battle-pass/schedule", "GET", new Dictionary<string, object?>());
        AddBearer(context, "p1");

        var executed = await ExecuteAsync(context, AuthMode.Required);

        Assert.True(executed);
        Assert.Null(context.Result);
    }

    private static async Task<bool> ExecuteAsync(ActionExecutingContext context, AuthMode mode)
    {
        var filter = new PlayerScopeFilter(
            Options.Create(new AuthOptions { Mode = mode }),
            NullLogger<PlayerScopeFilter>.Instance);
        var executed = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });
        return executed;
    }

    private static ActionExecutingContext CreateContext(string path, string method, Dictionary<string, object?> arguments)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/" + path;
        httpContext.Request.Method = method;
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], arguments, new object());
    }

    private static void AddBearer(ActionExecutingContext context, string playerId)
    {
        context.HttpContext.Request.Headers.Authorization = "Bearer token";
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", playerId),
            new Claim("player_id", playerId)
        ], "jwt"));
    }

    private sealed class PlayerCommand
    {
        public string PlayerId { get; init; } = string.Empty;
    }
}
