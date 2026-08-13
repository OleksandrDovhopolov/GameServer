namespace GameServer.Api.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task IssueAsync(string tokenHash, string playerId, DateTime expiresAt, CancellationToken ct);
    Task<string?> RotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt, CancellationToken ct);
    Task RevokeAllForPlayerAsync(string playerId, CancellationToken ct);
}
