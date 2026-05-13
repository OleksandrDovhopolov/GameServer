namespace GameServer.Api.Models.Domain;

public sealed class PlayerInventoryRecord
{
    public required string PlayerId { get; init; }
    public Dictionary<string, int> Items { get; init; } = new(StringComparer.Ordinal);
    public required long Version { get; set; }
}
