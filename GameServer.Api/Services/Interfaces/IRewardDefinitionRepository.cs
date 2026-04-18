using GameServer.Api.Models.Domain.Rewards;

namespace GameServer.Api.Services.Interfaces;

public interface IRewardDefinitionRepository
{
    Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct);
}
