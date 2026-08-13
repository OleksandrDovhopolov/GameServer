namespace GameServer.Api.Models.Dto.Auth;

public sealed class AnonymousAuthRequest
{
    public string? LegacyPlayerId { get; init; }
}

public sealed class RefreshAuthRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class ExternalAuthRequest
{
    public string Provider { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
}

public sealed class AuthTokenResponse
{
    public bool Success { get; init; } = true;
    public string PlayerId { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresInSeconds { get; init; }
}

public sealed class AuthErrorResponse
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}
