using System.Net;
using System.Text;
using GameServer.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Filters;

public sealed class AdminBasicAuthFilter : IAsyncAuthorizationFilter
{
    private const string AuthorizationHeaderName = "Authorization";
    private const string WwwAuthenticateHeaderName = "WWW-Authenticate";
    private const string WwwAuthenticateHeaderValue = "Basic realm=\"Admin API\"";

    private readonly AdminAuthOptions _options;
    private readonly ILogger<AdminBasicAuthFilter> _logger;

    public AdminBasicAuthFilter(IOptions<AdminAuthOptions> options, ILogger<AdminBasicAuthFilter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!IsAllowedIp(context.HttpContext.Connection.RemoteIpAddress))
        {
            _logger.LogWarning("Admin auth rejected request from non-whitelisted IP.");
            SetUnauthorized(context);
            return Task.CompletedTask;
        }

        var expectedUser = GetConfiguredUser();
        var expectedPass = GetConfiguredPassword();
        if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPass))
        {
            _logger.LogError("Admin auth rejected request because ADMIN_USER/ADMIN_PASS (or AdminAuth fallback) is not configured.");
            SetUnauthorized(context);
            return Task.CompletedTask;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(AuthorizationHeaderName, out var headerValue))
        {
            _logger.LogWarning("Admin auth rejected request due to missing Authorization header.");
            SetUnauthorized(context);
            return Task.CompletedTask;
        }

        if (!TryParseBasicCredentials(headerValue.ToString(), out var suppliedUser, out var suppliedPass))
        {
            _logger.LogWarning("Admin auth rejected request due to malformed Basic header.");
            SetUnauthorized(context);
            return Task.CompletedTask;
        }

        if (!string.Equals(suppliedUser, expectedUser, StringComparison.Ordinal) ||
            !string.Equals(suppliedPass, expectedPass, StringComparison.Ordinal))
        {
            _logger.LogWarning("Admin auth rejected request due to invalid credentials.");
            SetUnauthorized(context);
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private string? GetConfiguredUser()
    {
        return Environment.GetEnvironmentVariable("ADMIN_USER") ?? _options.Username;
    }

    private string? GetConfiguredPassword()
    {
        return Environment.GetEnvironmentVariable("ADMIN_PASS") ?? _options.Password;
    }

    private bool IsAllowedIp(IPAddress? remoteIpAddress)
    {
        var configuredAllowedIps = GetAllowedIpSet();
        if (configuredAllowedIps.Count == 0)
        {
            return true;
        }

        if (remoteIpAddress is null)
        {
            return false;
        }

        var normalized = NormalizeIp(remoteIpAddress);
        return configuredAllowedIps.Contains(normalized);
    }

    private HashSet<string> GetAllowedIpSet()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ADMIN_ALLOWED_IPS");
        var raw = string.IsNullOrWhiteSpace(fromEnv) ? _options.AllowedIps : fromEnv;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeIp);
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeIp(IPAddress ipAddress)
    {
        return NormalizeIp(ipAddress.ToString());
    }

    private static string NormalizeIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed))
        {
            return ip.Trim();
        }

        return parsed.MapToIPv4().ToString();
    }

    private static bool TryParseBasicCredentials(string authorizationHeader, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string basicPrefix = "Basic ";
        if (!authorizationHeader.StartsWith(basicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encoded = authorizationHeader[basicPrefix.Length..].Trim();
        if (encoded.Length == 0)
        {
            return false;
        }

        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        var decoded = Encoding.UTF8.GetString(decodedBytes);
        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        username = decoded[..separatorIndex];
        password = decoded[(separatorIndex + 1)..];
        return true;
    }

    private static void SetUnauthorized(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.Headers[WwwAuthenticateHeaderName] = WwwAuthenticateHeaderValue;
        context.Result = new UnauthorizedResult();
    }
}
