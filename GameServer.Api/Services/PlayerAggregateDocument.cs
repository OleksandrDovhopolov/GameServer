using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Rewards;

namespace GameServer.Api.Services;

public static class PlayerAggregateDocument
{
    public static PlayerStateSnapshotDto ToSnapshot(PlayerAggregate player)
    {
        return new PlayerStateSnapshotDto
        {
            Resources = new Dictionary<string, int>(player.Resources, StringComparer.Ordinal),
            InventoryItems = player.InventoryItems
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new InventoryItemDto { ItemId = x.Key, Amount = x.Value })
                .ToList(),
        };
    }
}
