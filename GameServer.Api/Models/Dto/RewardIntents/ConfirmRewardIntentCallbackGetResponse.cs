using GameServer.Api.Services;
using System.Text.Json.Serialization;

namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class ConfirmRewardIntentCallbackGetResponse
{
    public bool Success { get; init; }
    public string Status { get; init; } = RewardIntentStatuses.Failed;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RewardIntentActionResultDto? ActionResult { get; init; }
}
