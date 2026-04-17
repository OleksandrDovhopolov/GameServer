namespace GameServer.Api.Models.Domain;

public sealed class WheelSpinResult
{
    public required string RewardId { get; init; }
    public required int AvailableSpins { get; init; }
    public required long UpdatedAt { get; init; }
    public required long NextUpdateAt { get; init; }
}
