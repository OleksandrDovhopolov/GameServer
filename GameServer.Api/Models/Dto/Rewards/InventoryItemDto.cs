namespace GameServer.Api.Models.Dto.Rewards;

public sealed class InventoryItemDto
{
    public string ItemId { get; init; } = string.Empty;
    public int Amount { get; init; }
}
