using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class GameSaveAssembler : IGameSaveAssembler
{
    private const string ResourcesPropertyName = "Resources";
    private const string InventoryPropertyName = "Inventory";
    private const string InventoryItemsPropertyName = "InventoryItems";
    private const string ServerRewardsPropertyName = "ServerRewards";

    public string BuildSaveJson(PlayerAggregate player)
    {
        var root = player.RootDocument.DeepClone().AsObject();
        root.Remove(ServerRewardsPropertyName);

        var resources = new JsonObject();
        foreach (var resourceId in player.Resources.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            resources[resourceId] = player.Resources[resourceId];
        }

        var inventoryItems = new JsonObject();
        foreach (var itemId in player.InventoryItems.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            inventoryItems[itemId] = player.InventoryItems[itemId];
        }

        root[ResourcesPropertyName] = resources;
        root[InventoryPropertyName] = new JsonObject
        {
            [InventoryItemsPropertyName] = inventoryItems
        };

        return root.ToJsonString();
    }
}
