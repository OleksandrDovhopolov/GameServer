namespace GameServer.Api.Models.Domain.Auth;

public static class AuthProviders
{
    public const string Anonymous = "anonymous";
    public const string Legacy = "legacy";
}

public static class AuthErrorCodes
{
    public const string ProviderNotSupported = "provider_not_supported";
    public const string IdentityAlreadyLinked = "identity_already_linked";
    public const string LegacyClaimDisabled = "legacy_claim_disabled";
    public const string LegacyAlreadyClaimed = "legacy_already_claimed";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string AuthNotConfigured = "auth_not_configured";
    public const string ValidationError = "validation_error";
}

public sealed record PlayerAccount(
    string PlayerId,
    DateTime CreatedAt,
    DateTime? LastSeenAt);

public sealed record PlayerIdentity(
    string Provider,
    string Subject,
    string PlayerId,
    DateTime CreatedAt);

public sealed record ExternalIdentity(string Provider, string Subject);

public sealed record IssuedTokens(
    string PlayerId,
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds);

public sealed class AuthResult
{
    private AuthResult(bool success, IssuedTokens? tokens, string? errorCode, string? errorMessage)
    {
        Success = success;
        Tokens = tokens;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Success { get; }
    public IssuedTokens? Tokens { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static AuthResult Ok(IssuedTokens tokens) => new(true, tokens, null, null);

    public static AuthResult Fail(string errorCode, string errorMessage) => new(false, null, errorCode, errorMessage);
}
