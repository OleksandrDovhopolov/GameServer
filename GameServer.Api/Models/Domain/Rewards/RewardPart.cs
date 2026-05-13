namespace GameServer.Api.Models.Domain.Rewards;

public sealed class RewardPart
{
    public RewardKind Kind { get; init; } = RewardKind.Unknown;
    public string TargetId { get; init; } = string.Empty;
    public int Amount { get; init; }
    public string? Category { get; init; }
}
