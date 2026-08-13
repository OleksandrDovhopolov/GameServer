using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using GameServer.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Filters;

public sealed class PlayerScopeFilter : IAsyncActionFilter
{
    private const string AuthorizationHeaderName = "Authorization";
    private readonly AuthOptions _options;
    private readonly ILogger<PlayerScopeFilter> _logger;

    public PlayerScopeFilter(IOptions<AuthOptions> options, ILogger<PlayerScopeFilter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_options.Mode == AuthMode.Disabled || IsPublicRoute(context.HttpContext.Request))
        {
            await next();
            return;
        }

        var hasBearer = HasBearerToken(context.HttpContext.Request);
        var isAuthenticated = context.HttpContext.User.Identity?.IsAuthenticated == true;

        if (!hasBearer)
        {
            if (_options.Mode == AuthMode.Optional)
            {
                await next();
                return;
            }

            context.Result = new UnauthorizedResult();
            return;
        }

        if (!isAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var playerId = GetClaimPlayerId(context);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            _logger.LogWarning("Authenticated request did not contain a player subject claim.");
            context.Result = new UnauthorizedResult();
            return;
        }

        var requestedPlayerIds = GetRequestedPlayerIds(context.ActionArguments);
        if (requestedPlayerIds.Any(x => !string.Equals(x, playerId, StringComparison.Ordinal)))
        {
            _logger.LogWarning("Player scope mismatch. TokenPlayerId: {TokenPlayerId}, RequestedPlayerIds: {RequestedPlayerIds}",
                playerId,
                string.Join(",", requestedPlayerIds));
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }

    private static string? GetClaimPlayerId(ActionExecutingContext context)
    {
        return context.HttpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
               context.HttpContext.User.FindFirst("sub")?.Value ??
               context.HttpContext.User.FindFirst("player_id")?.Value;
    }

    private static List<string> GetRequestedPlayerIds(IDictionary<string, object?> actionArguments)
    {
        var playerIds = new List<string>();
        foreach (var (name, value) in actionArguments)
        {
            if (value is null)
            {
                continue;
            }

            if (value is string stringValue &&
                string.Equals(name, "playerId", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(stringValue))
            {
                playerIds.Add(stringValue.Trim());
                continue;
            }

            var property = value.GetType().GetProperty(
                "PlayerId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property?.PropertyType != typeof(string))
            {
                continue;
            }

            if (property.GetValue(value) is string propertyValue && !string.IsNullOrWhiteSpace(propertyValue))
            {
                playerIds.Add(propertyValue.Trim());
            }
        }

        return playerIds.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool HasBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(AuthorizationHeaderName, out var value))
        {
            return false;
        }

        return value.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicRoute(HttpRequest request)
    {
        var path = request.Path.Value?.TrimStart('/') ?? string.Empty;
        var method = request.Method;

        return path.Length == 0 ||
               string.Equals(path, "ping", StringComparison.OrdinalIgnoreCase) ||
               StartsWithSegment(path, "api/v1/auth") ||
               StartsWithSegment(path, "api/v1/configs") ||
               StartsWithSegment(path, "api/v1/liveops") ||
               StartsWithSegment(path, "api/admin") ||
               (HttpMethods.IsGet(method) &&
                string.Equals(path, "api/v1/rewards/intent/callback", StringComparison.OrdinalIgnoreCase)) ||
               (HttpMethods.IsGet(method) &&
                string.Equals(path, "api/v1/rewards/intent/status", StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithSegment(string path, string prefix)
    {
        return path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
