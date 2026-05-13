using GameServer.Api.Services;

namespace GameServer.Api.Models.Domain.RewardIntents;

public sealed class RewardIntentRecord
{
    public string RewardIntentId { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string RewardId { get; init; } = string.Empty;
    public string Status { get; init; } = RewardIntentStatuses.Pending;
    public long CreatedAtUtc { get; init; }
    public long UpdatedAtUtc { get; init; }
    public long ExpiresAtUtc { get; init; }
    public long? FulfilledAtUtc { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AdNetwork { get; init; }
    public string? ExternalEventId { get; init; }
}
