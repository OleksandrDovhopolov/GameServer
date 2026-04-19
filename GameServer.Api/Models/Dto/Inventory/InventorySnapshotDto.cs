namespace GameServer.Api.Models.Dto.Inventory;

public sealed class InventorySnapshotDto
{
    public Dictionary<string, int> Items { get; init; } = new(StringComparer.Ordinal);
}
