namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelSpinResponse
{
    public required string RewardId { get; init; }
    public required int AvailableSpins { get; init; }
    public required long UpdatedAt { get; init; }
    public required long NextUpdateAt { get; init; }
    public required GameServer.Api.Models.Dto.Rewards.GrantRewardResponse RewardGrant { get; init; }
}
