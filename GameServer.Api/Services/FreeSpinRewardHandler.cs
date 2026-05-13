using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class FreeSpinRewardHandler : IRewardHandler
{
    public bool CanHandle(RewardPart rewardPart)
    {
        return rewardPart.Kind == RewardKind.FreeSpin;
    }

    public Task HandleAsync(PlayerAggregate player, RewardPart rewardPart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (rewardPart.Amount != 1)
        {
            throw new InvalidOperationException("Free spin reward amount must be equal to one.");
        }

        // Free spin does not mutate player aggregate directly.
        return Task.CompletedTask;
    }
}
