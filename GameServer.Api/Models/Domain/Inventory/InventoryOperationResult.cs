using GameServer.Api.Models.Dto.Rewards;

namespace GameServer.Api.Models.Domain.Inventory;

public sealed class InventoryOperationResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public PlayerStateSnapshotDto? PlayerState { get; init; }
}
