namespace GameServer.Api.Models.Dto.Rewards;

public sealed class PlayerStateSnapshotDto
{
    public Dictionary<string, int> Resources { get; init; } = new(StringComparer.Ordinal);
    public List<InventoryItemDto> InventoryItems { get; init; } = new();
    public List<string> UnlockedAvatarIds { get; init; } = new();
}
