using GameServer.Api.Models.Domain.Rewards;

namespace GameServer.Api.Services.Interfaces;

public interface IRewardHandler
{
    bool CanHandle(RewardPart rewardPart);
    Task HandleAsync(PlayerAggregate player, RewardPart rewardPart, CancellationToken ct);
}
