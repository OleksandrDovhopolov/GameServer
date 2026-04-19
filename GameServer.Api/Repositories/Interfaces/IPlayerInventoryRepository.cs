using GameServer.Api.Models.Domain;

namespace GameServer.Api.Repositories.Interfaces;

public interface IPlayerInventoryRepository
{
    Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct);
    Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct);
}
