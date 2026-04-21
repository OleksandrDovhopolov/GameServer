namespace GameServer.Api.Repositories.Interfaces;

public interface IPlayerEconomyRepository
{
    Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct);
}
