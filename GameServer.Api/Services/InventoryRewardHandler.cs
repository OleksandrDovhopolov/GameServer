using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class InventoryRewardHandler : IRewardHandler
{
    public bool CanHandle(RewardPart rewardPart)
    {
        return rewardPart.Kind == RewardKind.InventoryItem;
    }

    public Task HandleAsync(PlayerAggregate player, RewardPart rewardPart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rewardPart.TargetId))
        {
            throw new InvalidOperationException("Inventory reward target id is required.");
        }

        if (rewardPart.Amount <= 0)
        {
            throw new InvalidOperationException("Inventory reward amount must be greater than zero.");
        }

        player.InventoryItems.TryGetValue(rewardPart.TargetId, out var existingAmount);
        player.InventoryItems[rewardPart.TargetId] = checked(existingAmount + rewardPart.Amount);
        return Task.CompletedTask;
    }
}
