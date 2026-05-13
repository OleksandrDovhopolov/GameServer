namespace GameServer.Api.Models.Dto.RewardIntents;

public sealed class CreateRewardIntentRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string RewardId { get; init; } = string.Empty;
}
