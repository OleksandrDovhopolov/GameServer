namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class CreateRewardIntentResponse
{
    public bool Success { get; init; }
    public string? RewardIntentId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
