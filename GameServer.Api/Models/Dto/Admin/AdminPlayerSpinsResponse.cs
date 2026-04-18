namespace GameServer.Api.Models.Dto.Admin;

public sealed class AdminPlayerSpinsResponse
{
    public required string PlayerId { get; init; }
    public required int AvailableSpins { get; init; }
    public required long UpdatedAt { get; init; }
    public required long NextUpdateAt { get; init; }
}
