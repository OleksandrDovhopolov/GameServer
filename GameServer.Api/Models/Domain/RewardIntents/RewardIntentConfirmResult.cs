using GameServer.Api.Services;

namespace GameServer.Api.Models.Domain.RewardIntents;

public sealed class RewardIntentConfirmResult
{
    public string ResultCode { get; init; } = RewardIntentErrorCodes.RewardGrantFailed;
    public string Status { get; init; } = RewardIntentStatuses.Failed;
    public string? PlayerId { get; init; }
    public string? RewardId { get; init; }
    public string? ErrorMessage { get; init; }
}
