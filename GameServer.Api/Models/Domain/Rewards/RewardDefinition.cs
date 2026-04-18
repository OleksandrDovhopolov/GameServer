namespace GameServer.Api.Models.Domain.Rewards;

public sealed class RewardDefinition
{
    public string RewardId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public List<RewardPart> Parts { get; init; } = new();
}
