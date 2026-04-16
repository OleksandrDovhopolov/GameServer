using GameServer.Api.Models.Domain;

namespace GameServer.Api.Repositories.Interfaces;

public interface ISaveRepository
{
    Task SaveAsync(string playerId, string data, CancellationToken cancellationToken);
    Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken);
    Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken);
    Task DeleteAsync(string playerId, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken);
}
