using System.Text.Json;

namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class ConfirmRewardIntentCallbackRequest
{
    public string Provider { get; init; } = string.Empty;
    public string RewardIntentId { get; init; } = string.Empty;
    public string ExternalEventId { get; init; } = string.Empty;
    public JsonElement? Payload { get; init; }
}
