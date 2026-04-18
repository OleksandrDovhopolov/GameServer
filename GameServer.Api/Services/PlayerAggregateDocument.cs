using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Rewards;

namespace GameServer.Api.Services;

public static class PlayerAggregateDocument
{
    private const string ServerRewardsPropertyName = "ServerRewards";
    private const string ResourcesPropertyName = "Resources";
    private const string InventoryItemsPropertyName = "InventoryItems";

    public static PlayerAggregate Deserialize(string playerId, string? payload)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(payload))
        {
            root = [];
        }
        else
        {
            var parsed = JsonNode.Parse(payload);
            if (parsed is not JsonObject jsonObject)
            {
                throw new InvalidOperationException("Stored player save root is not a JSON object.");
            }

            root = jsonObject;
        }

        var aggregate = new PlayerAggregate
        {
            PlayerId = playerId,
            RootDocument = root
        };

        if (!root.TryGetPropertyValue(ServerRewardsPropertyName, out var rewardsNode) ||
            rewardsNode is not JsonObject rewardsObject)
        {
            return aggregate;
        }

        ReadResourceMap(rewardsObject, ResourcesPropertyName, aggregate.Resources);
        ReadResourceMap(rewardsObject, InventoryItemsPropertyName, aggregate.InventoryItems);
        return aggregate;
    }

    public static void WriteBack(PlayerAggregate player)
    {
        var root = player.RootDocument;
        var rewardsObject = root.TryGetPropertyValue(ServerRewardsPropertyName, out var rewardsNode) &&
                            rewardsNode is JsonObject existingRewards
            ? existingRewards
            : new JsonObject();

        rewardsObject[ResourcesPropertyName] = BuildMapObject(player.Resources);
        rewardsObject[InventoryItemsPropertyName] = BuildMapObject(player.InventoryItems);
        root[ServerRewardsPropertyName] = rewardsObject;
    }

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

    private static void ReadResourceMap(JsonObject source, string propertyName, Dictionary<string, int> target)
    {
        if (!source.TryGetPropertyValue(propertyName, out var valueNode) || valueNode is not JsonObject valueObject)
        {
            return;
        }

        foreach (var (key, value) in valueObject)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                continue;
            }

            if (!TryReadInt(value, out var amount))
            {
                continue;
            }

            if (amount < 0)
            {
                continue;
            }

            target[key] = amount;
        }
    }

    private static JsonObject BuildMapObject(Dictionary<string, int> source)
    {
        var result = new JsonObject();
        foreach (var key in source.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            result[key] = source[key];
        }

        return result;
    }

    private static bool TryReadInt(JsonNode valueNode, out int value)
    {
        if (valueNode is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
        {
            value = intValue;
            return true;
        }

        if (valueNode is JsonValue jsonString &&
            jsonString.TryGetValue(out string? stringValue) &&
            int.TryParse(stringValue, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }
}
