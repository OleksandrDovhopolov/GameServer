namespace GameServer.Api.Models.Dto.Wheel;

public sealed class WheelSpinBlockedResponse
{
    public required string Code { get; init; }
    public required int NextRegenSeconds { get; init; }
}
