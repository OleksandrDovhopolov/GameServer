using System.Net;
using System.Text;
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

[Collection("AdminEnv")]
public sealed class AdminBasicAuthFilterTests
{
    [Fact]
    public async Task MissingAuthorizationHeader_ReturnsUnauthorizedWithWwwAuthenticate()
    {
        await RunWithAdminEnvironmentAsync("admin", "secret", () => ExecuteAndAssertUnauthorizedAsync(context =>
        {
            // header intentionally missing
        }, "admin", "secret"));
    }

    [Fact]
    public async Task MalformedAuthorizationHeader_ReturnsUnauthorized()
    {
        await RunWithAdminEnvironmentAsync("admin", "secret", () => ExecuteAndAssertUnauthorizedAsync(context =>
        {
            context.HttpContext.Request.Headers.Authorization = "Basic not-base64";
        }, "admin", "secret"));
    }

    [Fact]
    public async Task WrongCredentials_ReturnsUnauthorized()
    {
        await RunWithAdminEnvironmentAsync("admin", "secret", () => ExecuteAndAssertUnauthorizedAsync(context =>
        {
            context.HttpContext.Request.Headers.Authorization = CreateBasicHeader("admin", "wrong");
        }, "admin", "secret"));
    }

    [Fact]
    public async Task CorrectCredentials_AllowsRequest()
    {
        await RunWithAdminEnvironmentAsync("admin", "secret", async () =>
        {
            var context = CreateAuthorizationContext(IPAddress.Parse("127.0.0.1"));
            context.HttpContext.Request.Headers.Authorization = CreateBasicHeader("admin", "secret");

            var filter = CreateFilter(new AdminAuthOptions
            {
                Username = "fallback",
                Password = "fallback"
            });

            await filter.OnAuthorizationAsync(context);

            Assert.Null(context.Result);
        });
    }

    [Fact]
    public async Task WhitelistConfigured_RequestFromDifferentIp_ReturnsUnauthorized()
    {
        await RunWithAdminEnvironmentAsync("admin", "secret", async () =>
        {
            var context = CreateAuthorizationContext(IPAddress.Parse("127.0.0.1"));
            context.HttpContext.Request.Headers.Authorization = CreateBasicHeader("admin", "secret");

            var filter = CreateFilter(new AdminAuthOptions
            {
                AllowedIps = "10.0.0.1"
            });

            await filter.OnAuthorizationAsync(context);

            Assert.IsType<UnauthorizedResult>(context.Result);
        });
    }

    private static async Task ExecuteAndAssertUnauthorizedAsync(
        Action<AuthorizationFilterContext> arrange,
        string configuredUser,
        string configuredPass)
    {
        var context = CreateAuthorizationContext(IPAddress.Parse("127.0.0.1"));
        arrange(context);

        var filter = CreateFilter(new AdminAuthOptions
        {
            Username = configuredUser,
            Password = configuredPass
        });

        await filter.OnAuthorizationAsync(context);

        Assert.IsType<UnauthorizedResult>(context.Result);
        Assert.Equal("Basic realm=\"Admin API\"", context.HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    private static AdminBasicAuthFilter CreateFilter(AdminAuthOptions options)
    {
        return new AdminBasicAuthFilter(
            Options.Create(options),
            NullLogger<AdminBasicAuthFilter>.Instance);
    }

    private static AuthorizationFilterContext CreateAuthorizationContext(IPAddress remoteIp)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = remoteIp;
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static string CreateBasicHeader(string username, string password)
    {
        var raw = $"{username}:{password}";
        return $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))}";
    }

    private static async Task RunWithAdminEnvironmentAsync(
        string? adminUser,
        string? adminPass,
        Func<Task> action)
    {
        var oldUser = Environment.GetEnvironmentVariable("ADMIN_USER");
        var oldPass = Environment.GetEnvironmentVariable("ADMIN_PASS");
        var oldAllowedIps = Environment.GetEnvironmentVariable("ADMIN_ALLOWED_IPS");

        try
        {
            Environment.SetEnvironmentVariable("ADMIN_USER", adminUser);
            Environment.SetEnvironmentVariable("ADMIN_PASS", adminPass);
            Environment.SetEnvironmentVariable("ADMIN_ALLOWED_IPS", null);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_USER", oldUser);
            Environment.SetEnvironmentVariable("ADMIN_PASS", oldPass);
            Environment.SetEnvironmentVariable("ADMIN_ALLOWED_IPS", oldAllowedIps);
        }
    }
}
