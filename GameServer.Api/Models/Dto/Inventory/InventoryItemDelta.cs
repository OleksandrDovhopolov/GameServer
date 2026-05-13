namespace GameServer.Api.Models.Dto.Inventory;

public sealed class InventoryItemDelta
{
    public string ItemId { get; init; } = string.Empty;
    public int Amount { get; init; }
}
