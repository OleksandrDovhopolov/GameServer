namespace GameServer.Api.Configuration;

public sealed class AdminAuthOptions
{
    public string? Username { get; init; }
    public string? Password { get; init; }

    // Supports comma-separated values from configuration (AdminAuth:AllowedIps).
    public string? AllowedIps { get; init; }
}
