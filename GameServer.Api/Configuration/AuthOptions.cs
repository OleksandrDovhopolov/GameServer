namespace GameServer.Api.Configuration;

public enum AuthMode
{
    Disabled,
    Optional,
    Required
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public AuthMode Mode { get; init; } = AuthMode.Disabled;
    public string JwtSigningKey { get; init; } = string.Empty;
    public string KeyId { get; init; } = "default";
    public string Issuer { get; init; } = "gameserver";
    public int AccessTokenMinutes { get; init; } = 60;
    public int RefreshTokenDays { get; init; } = 90;
    public bool AllowLegacyClaim { get; init; } = true;
    public int AnonymousLimitPerMinute { get; init; } = 20;
    public int RefreshLimitPerMinute { get; init; } = 60;
}
