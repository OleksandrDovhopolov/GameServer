namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelDataResponse
{
    public required int AvailableSpins { get; init; }
    public required int NextRegenSeconds { get; init; }
}
