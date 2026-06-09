using System.Text.Json;
using GameServer.Api.Models.Domain.Config;
using GameServer.Api.Models.Dto.Configs;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public void ComputeEtag_ProducesConsistentHash()
    {
        var etag1 = ConfigService.ComputeEtag("[{\"id\":\"a\"}]");
        var etag2 = ConfigService.ComputeEtag("[{\"id\":\"a\"}]");

        Assert.Equal(etag1, etag2);
        Assert.Equal(12, etag1.Length);
    }

    [Fact]
    public void ComputeEtag_DifferentContent_ProducesDifferentHash()
    {
        var etag1 = ConfigService.ComputeEtag("[{\"id\":\"a\"}]");
        var etag2 = ConfigService.ComputeEtag("[{\"id\":\"b\"}]");

        Assert.NotEqual(etag1, etag2);
    }

    [Fact]
    public async Task GetManifestAsync_ReturnsLatestVersionsPerSection()
    {
        var repo = new InMemoryConfigRepository();
        await repo.AppendSeedAsync(MakeRecord("books", "prod", 1, "aaa"));
        await repo.AppendSeedAsync(MakeRecord("books", "prod", 2, "bbb"));
        await repo.AppendSeedAsync(MakeRecord("events", "prod", 1, "ccc"));
        var service = new ConfigService(repo);

        var manifest = await service.GetManifestAsync("prod", default);

        Assert.Equal(2, manifest.Length);
        Assert.Equal(2, manifest.Single(x => x.Name == "books").Version);
        Assert.Equal("bbb", manifest.Single(x => x.Name == "books").Etag);
        Assert.Equal(1, manifest.Single(x => x.Name == "events").Version);
    }

    [Fact]
    public async Task GetConfigAsync_WhenEtagMatches_ReturnsNotModified()
    {
        var repo = new InMemoryConfigRepository();
        await repo.AppendSeedAsync(MakeRecord("books", "prod", 1, "aaa"));
        var service = new ConfigService(repo);

        var result = await service.GetConfigAsync("books", "prod", "\"aaa\"", default);

        Assert.Equal(ConfigOperationStatus.NotModified, result.Status);
    }

    [Fact]
    public async Task GetConfigAsync_WhenSectionMissing_ReturnsNotFound()
    {
        var service = new ConfigService(new InMemoryConfigRepository());

        var result = await service.GetConfigAsync("books", "prod", null, default);

        Assert.Equal(ConfigOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task GetConfigAsync_WhenSectionExists_ReturnsContent()
    {
        var repo = new InMemoryConfigRepository();
        await repo.AppendSeedAsync(MakeRecord("books", "prod", 1, "aaa"));
        var service = new ConfigService(repo);

        var result = await service.GetConfigAsync("books", "prod", null, default);

        Assert.Equal(ConfigOperationStatus.Success, result.Status);
        Assert.Equal("aaa", result.Etag);
        Assert.Equal("[]", result.Json);
    }

    [Fact]
    public async Task GetAdminConfigVersionAsync_WhenVersionExists_ReturnsRequestedVersion()
    {
        var repo = new InMemoryConfigRepository();
        await repo.AppendSeedAsync(MakeRecord("books", "dev", 1, "etag-1"));
        await repo.AppendSeedAsync(MakeRecord("books", "dev", 2, "etag-2"));
        var service = new ConfigService(repo);

        var result = await service.GetAdminConfigVersionAsync("books", "dev", 1, default);

        Assert.Equal(ConfigOperationStatus.Success, result.Status);
        Assert.Equal(1, result.Config!.Version);
        Assert.Equal("etag-1", result.Config.Etag);
    }

    [Fact]
    public async Task GetAdminConfigVersionAsync_WhenVersionMissing_ReturnsNotFound()
    {
        var service = new ConfigService(new InMemoryConfigRepository());

        var result = await service.GetAdminConfigVersionAsync("books", "dev", 42, default);

        Assert.Equal(ConfigOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task PublishAsync_WhenNoExistingVersion_CreatesVersionOne()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;

        var result = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        Assert.Equal(ConfigOperationStatus.Success, result.Status);
        Assert.Equal(1, result.Config!.Version);
        Assert.Equal("books", result.Config.Name);
    }

    [Fact]
    public async Task PublishAsync_WhenEtagMatches_IncrementsVersion()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;

        var first = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);
        var second = await service.PublishAsync("books", "prod", json, $"\"{first.Config!.Etag}\"", "editor", null, default);

        Assert.Equal(ConfigOperationStatus.Success, second.Status);
        Assert.Equal(2, second.Config!.Version);
    }

    [Fact]
    public async Task PublishAsync_WhenEtagMismatch_ReturnsPreconditionFailed()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;
        await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        var result = await service.PublishAsync("books", "prod", json, "\"stale-etag\"", "editor", null, default);

        Assert.Equal(ConfigOperationStatus.PreconditionFailed, result.Status);
    }

    [Fact]
    public async Task PublishAsync_SameContentTwice_ProducesSameEtag()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;

        var first = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);
        var second = await service.PublishAsync("books", "prod", json, $"\"{first.Config!.Etag}\"", "editor", null, default);

        Assert.Equal(first.Config!.Etag, second.Config!.Etag);
    }

    [Fact]
    public async Task PublishAsync_WhenPayloadIsNotArray_ReturnsValidationFailed()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("{\"id\":\"x\"}").RootElement;

        var result = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        Assert.Equal(ConfigOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task PublishAsync_WhenPayloadHasMissingId_ReturnsValidationFailed()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"title\":\"x\"}]").RootElement;

        var result = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        Assert.Equal(ConfigOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task PublishAsync_WhenPayloadHasEmptyId_ReturnsValidationFailed()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"  \"}]").RootElement;

        var result = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        Assert.Equal(ConfigOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task PublishAsync_WhenPayloadHasDuplicateIds_ReturnsValidationFailed()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"},{\"Id\":\"x\"}]").RootElement;

        var result = await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        Assert.Equal(ConfigOperationStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task RollbackAsync_CreatesNewVersionWithOldContent()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var v1Json = JsonDocument.Parse("[{\"id\":\"v1\"}]").RootElement;
        var v2Json = JsonDocument.Parse("[{\"id\":\"v2\"}]").RootElement;

        var v1 = await service.PublishAsync("books", "prod", v1Json, string.Empty, "editor", null, default);
        await service.PublishAsync("books", "prod", v2Json, $"\"{v1.Config!.Etag}\"", "editor", null, default);

        var rollback = await service.RollbackAsync("books", "prod", 1, "editor", default);

        Assert.Equal(ConfigOperationStatus.Success, rollback.Status);
        Assert.Equal(3, rollback.Config!.Version);
        Assert.Equal(v1.Config.Etag, rollback.Config.Etag);
    }

    [Fact]
    public async Task RollbackAsync_WhenVersionNotFound_ReturnsNotFound()
    {
        var service = new ConfigService(new InMemoryConfigRepository());

        var result = await service.RollbackAsync("books", "prod", 99, "editor", default);

        Assert.Equal(ConfigOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task PromoteAsync_CopiesLatestDevToProd()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;
        var dev = await service.PublishAsync("books", "dev", json, string.Empty, "editor", null, default);

        var promoted = await service.PromoteAsync("books", "dev", "prod", "publisher", default);

        Assert.Equal(ConfigOperationStatus.Success, promoted.Status);
        Assert.Equal("prod", promoted.Config!.Environment);
        Assert.Equal(dev.Config!.Etag, promoted.Config.Etag);
        Assert.Equal(1, promoted.Config.Version);
    }

    [Fact]
    public async Task PromoteAsync_WhenSourceMissing_ReturnsNotFound()
    {
        var service = new ConfigService(new InMemoryConfigRepository());

        var result = await service.PromoteAsync("books", "dev", "prod", "publisher", default);

        Assert.Equal(ConfigOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ManifestEtag_MatchesGetConfigEtag()
    {
        var service = new ConfigService(new InMemoryConfigRepository());
        var json = JsonDocument.Parse("[{\"id\":\"x\"}]").RootElement;
        await service.PublishAsync("books", "prod", json, string.Empty, "editor", null, default);

        var manifest = await service.GetManifestAsync("prod", default);
        var config = await service.GetConfigAsync("books", "prod", null, default);

        Assert.Equal(manifest[0].Etag, config.Etag);
    }

    private static ConfigVersionRecord MakeRecord(string name, string environment, long version, string etag) =>
        new(name, environment, version, etag, "[]", "test", DateTime.UtcNow, null);

    private sealed class InMemoryConfigRepository : IConfigRepository
    {
        private readonly List<ConfigVersionRecord> _store = [];

        public Task<ConfigVersionRecord[]> GetManifestAsync(string environment, CancellationToken cancellationToken)
        {
            var latest = _store
                .Where(r => r.Environment == environment)
                .GroupBy(r => r.Name)
                .Select(g => g.MaxBy(r => r.Version)!)
                .ToArray();
            return Task.FromResult(latest);
        }

        public Task<ConfigVersionRecord?> GetLatestAsync(string name, string environment, CancellationToken cancellationToken)
        {
            var record = _store
                .Where(r => r.Name == name && r.Environment == environment)
                .MaxBy(r => r.Version);
            return Task.FromResult(record);
        }

        public Task<ConfigVersionRecord?> GetByVersionAsync(string name, string environment, long version, CancellationToken cancellationToken)
        {
            var record = _store.FirstOrDefault(r =>
                r.Name == name && r.Environment == environment && r.Version == version);
            return Task.FromResult(record);
        }

        public Task<ConfigVersionRecord[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken)
        {
            var history = _store
                .Where(r => r.Name == name && r.Environment == environment)
                .OrderByDescending(r => r.Version)
                .ToArray();
            return Task.FromResult(history);
        }

        public Task<ConfigAppendResult> AppendAsync(ConfigAppendRequest request, CancellationToken cancellationToken)
        {
            var current = _store
                .Where(r => r.Name == request.Name && r.Environment == request.Environment)
                .MaxBy(r => r.Version);

            if (request.ExpectedCurrentEtag is not null &&
                current is not null &&
                !string.Equals(current.Etag, request.ExpectedCurrentEtag, StringComparison.Ordinal))
            {
                return Task.FromResult(ConfigAppendResult.PreconditionFailed());
            }

            var record = new ConfigVersionRecord(
                request.Name,
                request.Environment,
                (current?.Version ?? 0) + 1,
                request.Etag,
                request.Json,
                request.UpdatedBy,
                request.UpdatedAt,
                request.Comment);

            _store.Add(record);
            return Task.FromResult(ConfigAppendResult.Success(record));
        }

        public Task AppendSeedAsync(ConfigVersionRecord record)
        {
            _store.Add(record);
            return Task.CompletedTask;
        }
    }
}
