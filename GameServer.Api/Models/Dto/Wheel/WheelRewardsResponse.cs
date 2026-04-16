namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelRewardsResponse
{
    public required List<WheelRewardDto> Rewards { get; init; }
}
