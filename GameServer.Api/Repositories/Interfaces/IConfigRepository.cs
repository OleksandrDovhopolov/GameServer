using GameServer.Api.Models.Domain.Config;

namespace GameServer.Api.Repositories.Interfaces;

public interface IConfigRepository
{
    Task<ConfigVersionRecord[]> GetManifestAsync(string environment, CancellationToken cancellationToken);
    Task<ConfigVersionRecord?> GetLatestAsync(string name, string environment, CancellationToken cancellationToken);
    Task<ConfigVersionRecord?> GetByVersionAsync(string name, string environment, long version, CancellationToken cancellationToken);
    Task<ConfigVersionRecord[]> GetHistoryAsync(string name, string environment, CancellationToken cancellationToken);
    Task<ConfigAppendResult> AppendAsync(ConfigAppendRequest request, CancellationToken cancellationToken);
}
