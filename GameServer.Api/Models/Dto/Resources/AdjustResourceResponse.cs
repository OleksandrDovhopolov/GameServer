namespace GameServer.Api.Models.Dto.Resources;

public sealed class AdjustResourceResponse
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public ResourceSnapshotDto? Resources { get; init; }
}
