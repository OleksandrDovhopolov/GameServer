namespace GameServer.Api.Models.Domain;

public sealed class WheelStateResult
{
    public required int AvailableSpins { get; init; }
    public required int NextRegenSeconds { get; init; }
}
