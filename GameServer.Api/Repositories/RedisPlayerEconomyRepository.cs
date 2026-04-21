using GameServer.Api.Repositories.Interfaces;

namespace GameServer.Api.Repositories;

public sealed class RedisPlayerEconomyRepository : IPlayerEconomyRepository
{
    private readonly ISaveRepository _saveRepository;

    public RedisPlayerEconomyRepository(ISaveRepository saveRepository)
    {
        _saveRepository = saveRepository;
    }

    public Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct)
    {
        return _saveRepository.ExistsAsync(playerId, ct);
    }
}
