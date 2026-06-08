namespace GameServer.Api.Models.Domain.Config;

public enum ConfigAppendStatus
{
    Success,
    PreconditionFailed
}

public sealed class ConfigAppendResult
{
    private ConfigAppendResult(ConfigAppendStatus status, ConfigVersionRecord? record = null)
    {
        Status = status;
        Record = record;
    }

    public ConfigAppendStatus Status { get; }

    public ConfigVersionRecord? Record { get; }

    public static ConfigAppendResult Success(ConfigVersionRecord record) =>
        new(ConfigAppendStatus.Success, record);

    public static ConfigAppendResult PreconditionFailed() =>
        new(ConfigAppendStatus.PreconditionFailed);
}
