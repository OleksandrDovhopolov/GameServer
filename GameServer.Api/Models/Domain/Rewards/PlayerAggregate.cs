using System.Text.Json.Nodes;

namespace GameServer.Api.Models.Domain.Rewards;

public sealed class PlayerAggregate
{
    public string PlayerId { get; init; } = string.Empty;
    public JsonObject RootDocument { get; init; } = new();
    public long LastModified { get; set; }
    public Dictionary<string, int> Resources { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> InventoryItems { get; init; } = new(StringComparer.Ordinal);

    public void AddResource(string resourceId, int amount)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException("Resource id is required.");
        }

        if (amount <= 0)
        {
            throw new InvalidOperationException("Resource amount must be greater than zero.");
        }

        Resources.TryGetValue(resourceId, out var existingAmount);
        Resources[resourceId] = checked(existingAmount + amount);
    }

    public void AddInventoryItem(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new InvalidOperationException("Inventory item id is required.");
        }

        if (amount <= 0)
        {
            throw new InvalidOperationException("Inventory amount must be greater than zero.");
        }

        InventoryItems.TryGetValue(itemId, out var existingAmount);
        InventoryItems[itemId] = checked(existingAmount + amount);
    }
}
