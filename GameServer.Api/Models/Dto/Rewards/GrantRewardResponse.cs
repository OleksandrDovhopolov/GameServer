namespace GameServer.Api.Models.Dto.Rewards;

public sealed class GrantRewardResponse
{
    public bool Success { get; init; }
    public string RewardId { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public PlayerStateSnapshotDto? PlayerState { get; init; }
}
