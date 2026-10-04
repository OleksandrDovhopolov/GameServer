namespace GameServer.EventsWorker.Configuration;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";

    public string Topic { get; init; } = "game.events.v1";

    /// <summary>Events that cannot be parsed are forwarded here instead of blocking the partition.</summary>
    public string DeadLetterTopic { get; init; } = "game.events.v1.dlt";

    /// <summary>All instances sharing this id form one consumer group and split the partitions.</summary>
    public string GroupId { get; init; } = "stats-writer";
}
