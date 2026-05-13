using GameServer.Api.Models.Domain.Rewards;

namespace GameServer.Api.Repositories.Interfaces;

public interface IPlayerAggregateRepository
{
    Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct);
    Task SaveAsync(PlayerAggregate player, CancellationToken ct);
    Task DeleteAsync(string playerId, CancellationToken ct);
}
