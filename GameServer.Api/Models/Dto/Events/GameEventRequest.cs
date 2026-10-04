using System.Text.Json;

namespace GameServer.Api.Models.Dto.Events;

public sealed class GameEventRequest
{
    public string PlayerId { get; init; } = string.Empty;

    public string Event { get; init; } = string.Empty;

    public JsonElement? Payload { get; init; }
}
