namespace GameServer.Api.Models.Dto.Inventory;

public sealed class RemoveInventoryBatchCommand
{
    public string PlayerId { get; init; } = string.Empty;
    public List<InventoryItemDelta> Items { get; init; } = new();
    public string Reason { get; init; } = string.Empty;
}
