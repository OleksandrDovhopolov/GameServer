namespace GameServer.Api.Models.Dto.Rewards;

public sealed class GrantRewardCommand
{
    public string PlayerId { get; init; } = string.Empty;
    public string RewardSource { get; init; } = string.Empty;
    public string RewardId { get; init; } = string.Empty;
}
