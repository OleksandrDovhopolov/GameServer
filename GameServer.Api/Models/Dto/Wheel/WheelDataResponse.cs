namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelDataResponse
{
    public required int AvailableSpins { get; init; }
    public required long UpdatedAt { get; init; }
    public required long NextUpdateAt { get; init; }
}
