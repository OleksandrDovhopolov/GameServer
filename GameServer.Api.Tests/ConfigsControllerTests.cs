using System.Text.Json;
using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class ConfigsControllerTests
{
    [Fact]
    public async Task GetManifest_WithoutEnvironment_UsesProd()
    {
        var service = new StubConfigService();
        var controller = CreateController(service);

        var result = await controller.GetManifest(null, CancellationToken.None);

        Assert.Equal("prod", service.LastManifestEnvironment);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetManifest_WithEnvironment_UsesExplicitValue()
    {
        var service = new StubConfigService();
        var controller = CreateController(service);

        await controller.GetManifest("dev", CancellationToken.None);

        Assert.Equal("dev", service.LastManifestEnvironment);
    }

    [Fact]
    public async Task GetConfig_WhenSuccess_ReturnsJsonAndEtag()
    {
        var service = new StubConfigService
        {
            GetConfigResult = ConfigContentResult.Success("abc", "[{\"id\":\"x\"}]")
        };
        var controller = CreateController(service);

        var result = await controller.GetConfig("books", null, CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("[{\"id\":\"x\"}]", content.Content);
        Assert.Equal("application/json", content.ContentType);
        Assert.Equal("\"abc\"", controller.Response.Headers.ETag.ToString());
        Assert.Equal("prod", service.LastConfigEnvironment);
    }

    [Fact]
    public async Task GetConfig_WhenNotModified_Returns304()
    {
        var service = new StubConfigService
        {
            GetConfigResult = ConfigContentResult.NotModified()
        };
        var controller = CreateController(service);

        var result = await controller.GetConfig("books", "dev", CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
        Assert.Equal("dev", service.LastConfigEnvironment);
    }

    [Fact]
    public async Task GetConfig_WhenMissing_ReturnsNotFound()
    {
        var service = new StubConfigService
        {
            GetConfigResult = ConfigContentResult.NotFound()
        };
        var controller = CreateController(service);

        var result = await controller.GetConfig("books", null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    private static ConfigsController CreateController(StubConfigService service)
    {
        return new ConfigsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class StubConfigService : IConfigService
    {
        public string? LastManifestEnvironment { get; private set; }

        public string? LastConfigEnvironment { get; private set; }

        public ConfigContentResult GetConfigResult { get; set; } = ConfigContentResult.NotFound();

        public Task<ConfigManifestEntryDto[]> GetManifestAsync(string environment, CancellationToken cancellationToken)
        {
            LastManifestEnvironment = environment;
            return Task.FromResult(Array.Empty<ConfigManifestEntryDto>());
        }

        public Task<ConfigContentResult> GetConfigAsync(string name, string environment, string? ifNoneMatch, CancellationToken cancellationToken)
        {
            LastConfigEnvironment = environment;
            return Task.FromResult(GetConfigResult);
        }

        public Task<AdminConfigOperationResult> GetAdminConfigAsync(string name, string environment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> GetAdminConfigVersionAsync(string name, string environment, long version, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ConfigHistoryEntryDto[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> PublishAsync(string name, string environment, JsonElement json, string ifMatch, string updatedBy, string? comment, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> RollbackAsync(string name, string environment, long toVersion, string updatedBy, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<AdminConfigOperationResult> PromoteAsync(string name, string fromEnvironment, string toEnvironment, string updatedBy, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
