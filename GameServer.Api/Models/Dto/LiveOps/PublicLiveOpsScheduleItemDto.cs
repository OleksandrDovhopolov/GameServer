using System.Text.Json.Serialization;

namespace GameServer.Api.Models.Dto.LiveOps;

public sealed class PublicLiveOpsScheduleItemDto
{
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("EventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("StartTimeUtc")]
    public string StartTimeUtc { get; init; } = string.Empty;

    [JsonPropertyName("EndTimeUtc")]
    public string EndTimeUtc { get; init; } = string.Empty;

    [JsonPropertyName("Priority")]
    public int Priority { get; init; }

    [JsonPropertyName("StreamId")]
    public string StreamId { get; init; } = string.Empty;

    [JsonPropertyName("CustomParams")]
    public Dictionary<string, string> CustomParams { get; init; } = new(StringComparer.Ordinal);
}
