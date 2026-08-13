using GameServer.Api.Models.Domain.Auth;

namespace GameServer.Api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResult> AuthenticateAnonymousAsync(string? legacyPlayerId, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task<AuthResult> LoginAsync(string provider, string token, CancellationToken ct);
    Task<AuthResult> LinkAsync(string playerId, string provider, string token, CancellationToken ct);
}
