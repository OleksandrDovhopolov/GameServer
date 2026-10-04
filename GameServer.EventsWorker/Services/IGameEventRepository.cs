using GameServer.EventsWorker.Models;

namespace GameServer.EventsWorker.Services;

public interface IGameEventRepository
{
    Task InsertAsync(int partition, long offset, GameEventEnvelope envelope, CancellationToken cancellationToken);
}
