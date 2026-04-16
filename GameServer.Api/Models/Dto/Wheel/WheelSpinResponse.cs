namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelSpinResponse
{
    public required string RewardId { get; init; }
    public required int AvailableSpins { get; init; }
    public required int NextRegenSeconds { get; init; }
}
