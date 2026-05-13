namespace GameServer.Api.Models.Domain;

public sealed class PlayerResourcesRecord
{
    public required string PlayerId { get; init; }
    public Dictionary<string, int> Resources { get; init; } = new(StringComparer.Ordinal);
    public required long Version { get; set; }
}
