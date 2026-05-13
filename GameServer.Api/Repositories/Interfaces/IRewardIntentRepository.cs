using GameServer.Api.Models.Domain.RewardIntents;

namespace GameServer.Api.Repositories.Interfaces;

public interface IRewardIntentRepository
{
    Task<bool> CreateAsync(RewardIntentRecord intent, CancellationToken ct);
    Task<RewardIntentRecord?> GetByIdAsync(string rewardIntentId, CancellationToken ct);
    Task<bool> ExpireIfPendingAsync(string rewardIntentId, long nowUtcMs, string errorCode, string errorMessage, CancellationToken ct);
    Task<RewardIntentConfirmResult> TryBeginCallbackProcessingAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        long nowUtcMs,
        string lockToken,
        CancellationToken ct);
    Task<bool> FinalizeCallbackProcessingAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string lockToken,
        string finalStatus,
        string errorCode,
        string errorMessage,
        long nowUtcMs,
        CancellationToken ct);
}
