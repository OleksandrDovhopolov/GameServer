namespace GameServer.Api.Configuration;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>
    /// Disabled by default so a deployed environment without a broker never tries to connect.
    /// Enabled only in appsettings.Development.json.
    /// </summary>
    public bool Enabled { get; init; }

    public string BootstrapServers { get; init; } = string.Empty;

    public string Topic { get; init; } = "game.events.v1";

    public int ProduceTimeoutSeconds { get; init; } = 5;
}
