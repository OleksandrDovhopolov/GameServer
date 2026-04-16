namespace GameServer.Api.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";
}
