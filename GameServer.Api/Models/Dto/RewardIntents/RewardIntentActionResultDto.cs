using GameServer.Api.Models.Dto.Rewards;

namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class RewardIntentActionResultDto
{
    public string RewardId { get; init; } = string.Empty;
    public Dictionary<string, int> ResourceDelta { get; init; } = new(StringComparer.Ordinal);
    public List<InventoryItemDto> InventoryItems { get; init; } = new();
    public string? WheelSpinId { get; init; }
}
