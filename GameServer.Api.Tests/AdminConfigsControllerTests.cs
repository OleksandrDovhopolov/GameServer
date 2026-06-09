using System.Reflection;
using System.Text.Json;
using GameServer.Api.Controllers;
using GameServer.Api.Filters;
using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class AdminConfigsControllerTests
{
    [Fact]
    public void Route_UsesApiAdminConfigsWithoutV1()
    {
        var route = typeof(AdminConfigsController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.Equal("api/admin/configs", route!.Template);
    }

    [Fact]
    public async Task Publish_WhenValidationFails_ReturnsBadRequest()
    {
        var service = new StubConfigService
        {
            PublishResult = AdminConfigOperationResult.ValidationFailed("bad payload")
        };
        var controller = CreateController(service);
        controller.Request.Headers.Authorization = CreateBasicHeader("admin", "secret");
        controller.Request.Headers.IfMatch = "\"abc\"";
        controller.HttpContext.Items[AdminBasicAuthFilter.AuthenticatedUserItemKey] = "admin";

        var result = await controller.Publish(
            "books",
            "dev",
            "\"abc\"",
            new PublishConfigRequest { Json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("bad payload", badRequest.Value);
    }

    [Fact]
    public async Task Publish_WhenPreconditionFails_Returns412()
    {
        var service = new StubConfigService
        {
            PublishResult = AdminConfigOperationResult.PreconditionFailed("stale")
        };
        var controller = CreateController(service);
        controller.Request.Headers.Authorization = CreateBasicHeader("admin", "secret");
        controller.Request.Headers.IfMatch = "\"abc\"";
        controller.HttpContext.Items[AdminBasicAuthFilter.AuthenticatedUserItemKey] = "admin";

        var result = await controller.Publish(
            "books",
            "dev",
            "\"abc\"",
            new PublishConfigRequest { Json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement },
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, status.StatusCode);
    }

    [Fact]
    public async Task GetConfigVersion_WhenFound_ReturnsOkAndEtag()
    {
        var service = new StubConfigService
        {
            VersionResult = AdminConfigOperationResult.Success(new AdminConfigDto
            {
                Name = "books",
                Environment = "dev",
                Version = 3,
                Etag = "etag-3",
                Json = JsonDocument.Parse("[{\"id\":\"book_dune\"}]").RootElement
            })
        };
        var controller = CreateController(service);

        var result = await controller.GetConfigVersion("books", 3, "dev", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<AdminConfigDto>(ok.Value);
        Assert.Equal(3, dto.Version);
        Assert.Equal("etag-3", dto.Etag);
        Assert.False(controller.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task GetConfigVersion_WhenMissing_ReturnsNotFound()
    {
        var service = new StubConfigService
        {
            VersionResult = AdminConfigOperationResult.NotFound()
        };
        var controller = CreateController(service);

        var result = await controller.GetConfigVersion("books", 999, "dev", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetConfigVersion_WhenEnvironmentMissing_ReturnsBadRequest()
    {
        var service = new StubConfigService();
        var controller = CreateController(service);

        var result = await controller.GetConfigVersion("books", 3, string.Empty, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static AdminConfigsController CreateController(StubConfigService service)
    {
        return new AdminConfigsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static string CreateBasicHeader(string username, string password)
    {
        var raw = $"{username}:{password}";
        return $"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))}";
    }

    private sealed class StubConfigService : IConfigService
    {
        public AdminConfigOperationResult PublishResult { get; set; } = AdminConfigOperationResult.NotFound();

        public AdminConfigOperationResult VersionResult { get; set; } = AdminConfigOperationResult.NotFound();

        public Task<ConfigManifestEntryDto[]> GetManifestAsync(string environment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ConfigContentResult> GetConfigAsync(string name, string environment, string? ifNoneMatch, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> GetAdminConfigAsync(string name, string environment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> GetAdminConfigVersionAsync(string name, string environment, long version, CancellationToken cancellationToken) =>
            Task.FromResult(VersionResult);

        public Task<ConfigHistoryEntryDto[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> PublishAsync(string name, string environment, JsonElement json, string ifMatch, string updatedBy, string? comment, CancellationToken cancellationToken) =>
            Task.FromResult(PublishResult);

        public Task<AdminConfigOperationResult> RollbackAsync(string name, string environment, long toVersion, string updatedBy, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> PromoteAsync(string name, string fromEnvironment, string toEnvironment, string updatedBy, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
