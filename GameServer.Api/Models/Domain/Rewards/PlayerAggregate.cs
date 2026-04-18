using System.Text.Json.Nodes;

namespace GameServer.Api.Models.Domain.Rewards;

public sealed class PlayerAggregate
{
    public string PlayerId { get; init; } = string.Empty;
    public JsonObject RootDocument { get; init; } = new();
    public Dictionary<string, int> Resources { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> InventoryItems { get; init; } = new(StringComparer.Ordinal);
}
