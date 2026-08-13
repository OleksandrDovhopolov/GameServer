using GameServer.Api.Models.Domain.Auth;

namespace GameServer.Api.Services.Interfaces;

public interface IExternalIdentityVerifier
{
    string Provider { get; }
    Task<ExternalIdentity?> VerifyAsync(string token, CancellationToken ct);
}
