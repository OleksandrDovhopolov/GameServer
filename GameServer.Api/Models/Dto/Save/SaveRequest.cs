namespace GameServer.Api.Models.Dto.Save;

public sealed class SaveRequest
{
    public required string PlayerId { get; init; }
    public required string Data { get; init; }
}
