using GameServer.Api.Models.Domain;

namespace GameServer.Api.Repositories.Interfaces;

public interface IPlayerResourcesRepository
{
    Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct);
    Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct);
}
