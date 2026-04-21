using GameServer.Api.Models.Domain.RewardIntents;

namespace GameServer.Api.Repositories.Interfaces;

public interface IRewardIntentRepository
{
    Task<bool> CreateAsync(RewardIntentRecord intent, CancellationToken ct);
    Task<RewardIntentRecord?> GetByIdAsync(string rewardIntentId, CancellationToken ct);
    Task<bool> ExpireIfPendingAsync(string rewardIntentId, long nowUtcMs, string errorCode, string errorMessage, CancellationToken ct);
    Task<RewardIntentConfirmResult> ConfirmFromCallbackAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string expectedRewardId,
        int rewardAmount,
        long nowUtcMs,
        CancellationToken ct);
}
