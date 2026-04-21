using GameServer.Api.Services;

namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class GetRewardIntentStatusResponse
{
    public string Status { get; init; } = RewardIntentStatuses.Failed;
    public int? NewCrystalsBalance { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
