namespace GameServer.Api.Models.Dto.Inventory;

public sealed class RemoveInventoryItemCommand
{
    public string PlayerId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public int Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
}
