namespace GameServer.Api.Services.Interfaces;

public interface IJwtTokenService
{
    string IssueAccessToken(string playerId, DateTime utcNow, out DateTime expiresAt);
}
