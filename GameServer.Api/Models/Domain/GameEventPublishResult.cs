namespace GameServer.Api.Models.Domain;

/// <summary>
/// Where the event landed in Kafka. Partition and Offset are -1 when publishing is disabled.
/// </summary>
public sealed record GameEventPublishResult(bool Published, int Partition, long Offset);
