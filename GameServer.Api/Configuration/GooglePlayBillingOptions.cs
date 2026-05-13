namespace GameServer.Api.Configuration;

public sealed class GooglePlayBillingOptions
{
    public const string SectionName = "GooglePlayBilling";

    public bool Enabled { get; init; }
    public string PackageName { get; init; } = string.Empty;
    public string ServiceAccountJson { get; init; } = string.Empty;
    public string ServiceAccountKeyFile { get; init; } = string.Empty;
    public string ApplicationName { get; init; } = "GameServer.Api";
    public int HttpTimeoutSeconds { get; init; } = 30;
}
