namespace GameServer.Api.Configuration;

public enum SaveStorageProvider
{
    Redis,
    DualWriteReadRedis,
    DualWriteReadPostgres,
    Postgres
}

public sealed class SaveStorageOptions
{
    public const string SectionName = "SaveStorage";

    public SaveStorageProvider Provider { get; init; } = SaveStorageProvider.Redis;
}
