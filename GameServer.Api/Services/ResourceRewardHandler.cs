using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class ResourceRewardHandler : IRewardHandler
{
    public bool CanHandle(RewardPart rewardPart)
    {
        return rewardPart.Kind == RewardKind.Resource;
    }

    public Task HandleAsync(PlayerAggregate player, RewardPart rewardPart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rewardPart.TargetId))
        {
            throw new InvalidOperationException("Resource reward target id is required.");
        }

        if (rewardPart.Amount <= 0)
        {
            throw new InvalidOperationException("Resource reward amount must be greater than zero.");
        }

        player.Resources.TryGetValue(rewardPart.TargetId, out var existingAmount);
        player.Resources[rewardPart.TargetId] = checked(existingAmount + rewardPart.Amount);
        return Task.CompletedTask;
    }
}
