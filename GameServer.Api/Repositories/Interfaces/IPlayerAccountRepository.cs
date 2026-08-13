using GameServer.Api.Models.Domain.Auth;

namespace GameServer.Api.Repositories.Interfaces;

public interface IPlayerAccountRepository
{
    Task<PlayerAccount?> GetByIdentityAsync(string provider, string subject, CancellationToken ct);
    Task<PlayerAccount?> GetByPlayerIdAsync(string playerId, CancellationToken ct);
    Task<bool> CreateWithIdentityAsync(string playerId, string provider, string subject, CancellationToken ct);
    Task<bool> LinkIdentityAsync(string playerId, string provider, string subject, CancellationToken ct);
    Task TouchLastSeenAsync(string playerId, CancellationToken ct);
}
