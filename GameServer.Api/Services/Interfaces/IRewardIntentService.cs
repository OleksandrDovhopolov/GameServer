using GameServer.Api.Models.Dto.RewardIntents;

namespace GameServer.Api.Services.Interfaces;

public interface IRewardIntentService
{
    Task<CreateRewardIntentResponse> CreateIntentAsync(string playerId, string rewardId, CancellationToken ct = default);
    Task<GetRewardIntentStatusResponse> GetIntentStatusAsync(string rewardIntentId, CancellationToken ct = default);
    Task<ConfirmRewardIntentCallbackResponse> ConfirmIntentFromAdCallbackAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string? callbackPayload,
        CancellationToken ct = default);
}
