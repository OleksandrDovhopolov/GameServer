using GameServer.Api.Models.Dto.Configs;
using System.Text.Json;

namespace GameServer.Api.Services.Interfaces;

public interface IConfigService
{
    Task<ConfigManifestEntryDto[]> GetManifestAsync(string environment, CancellationToken cancellationToken);

    Task<ConfigContentResult> GetConfigAsync(string name, string environment, string? ifNoneMatch, CancellationToken cancellationToken);

    Task<AdminConfigOperationResult> GetAdminConfigAsync(string name, string environment, CancellationToken cancellationToken);

    Task<AdminConfigOperationResult> GetAdminConfigVersionAsync(string name, string environment, long version, CancellationToken cancellationToken);

    Task<ConfigHistoryEntryDto[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken);

    Task<AdminConfigOperationResult> PublishAsync(string name, string environment, JsonElement json, string ifMatch, string updatedBy, string? comment, CancellationToken cancellationToken);

    Task<AdminConfigOperationResult> RollbackAsync(string name, string environment, long toVersion, string updatedBy, CancellationToken cancellationToken);

    Task<AdminConfigOperationResult> PromoteAsync(string name, string fromEnvironment, string toEnvironment, string updatedBy, CancellationToken cancellationToken);
}
