using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface ISaveService
{
    Task SaveAsync(string playerId, string data, CancellationToken cancellationToken);
    Task<SaveRecord> LoadAsync(string playerId, CancellationToken cancellationToken);
    Task DeleteAsync(string playerId, CancellationToken cancellationToken);
    Task<long> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken);
}
