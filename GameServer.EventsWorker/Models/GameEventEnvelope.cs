using System.Text.Json.Serialization;

namespace GameServer.EventsWorker.Models;

/// <summary>Shape produced by GameServer.Api's KafkaGameEventPublisher.</summary>
public sealed class GameEventEnvelope
{
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("playerId")]
    public string? PlayerId { get; init; }

    [JsonPropertyName("occurredAtUtc")]
    public DateTimeOffset? OccurredAtUtc { get; init; }

    [JsonPropertyName("payload")]
    public System.Text.Json.JsonElement? Payload { get; init; }
}
