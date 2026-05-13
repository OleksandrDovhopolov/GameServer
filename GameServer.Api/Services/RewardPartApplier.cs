using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class RewardPartApplier : IRewardPartApplier
{
    private readonly IReadOnlyList<IRewardHandler> _handlers;

    public RewardPartApplier(IEnumerable<IRewardHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public async Task ApplyAsync(PlayerAggregate player, RewardDefinition rewardDefinition, CancellationToken ct)
    {
        foreach (var rewardPart in rewardDefinition.Parts)
        {
            var handler = _handlers.FirstOrDefault(x => x.CanHandle(rewardPart));
            if (handler is null)
            {
                throw new InvalidOperationException($"{RewardErrorCodes.UnsupportedRewardKind}:{rewardPart.Kind}");
            }

            await handler.HandleAsync(player, rewardPart, ct);
        }
    }
}
