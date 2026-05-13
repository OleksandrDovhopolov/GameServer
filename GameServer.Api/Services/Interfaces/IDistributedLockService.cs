namespace GameServer.Api.Services.Interfaces;

public interface IDistributedLockService
{
    Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}
