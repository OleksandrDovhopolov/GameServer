using GameServer.Api.Models.Dto.Resources;

namespace GameServer.Api.Models.Domain.Resources;

public sealed class AdjustResourceResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public ResourceSnapshotDto? Snapshot { get; init; }
}
