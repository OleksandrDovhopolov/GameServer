using GameServer.Api.Models.Domain.Rewards;

namespace GameServer.Api.Services.Interfaces;

public interface IRewardPartApplier
{
    Task ApplyAsync(PlayerAggregate player, RewardDefinition rewardDefinition, CancellationToken ct);
}
