namespace GameServer.Api.Configuration;

public sealed class AdminToolsOptions
{
    public const string SectionName = "AdminTools";

    public bool AllowRedisFlush { get; init; }
    public bool AllowPlayerSaveReset { get; init; }
}
