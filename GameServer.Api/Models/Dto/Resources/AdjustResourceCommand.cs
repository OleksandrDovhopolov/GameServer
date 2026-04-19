namespace GameServer.Api.Models.Dto.Resources;

public sealed class AdjustResourceCommand
{
    public string PlayerId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public int Delta { get; init; }
    public string Reason { get; init; } = string.Empty;
}
