namespace GameServer.Api.Models.Domain;

public sealed class WheelStateResult
{
    public required int AvailableSpins { get; init; }
    public required long UpdatedAt { get; init; }
    public required long NextUpdateAt { get; init; }
    public required bool AdSpinAvailable { get; init; }
}
