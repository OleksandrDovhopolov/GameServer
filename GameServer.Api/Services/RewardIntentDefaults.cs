namespace GameServer.Api.Services;

public static class RewardIntentDefaults
{
    public const string SupportedRewardId = "Gems";
    public const int GemsAmount = 50;
    public static readonly TimeSpan IntentTtl = TimeSpan.FromMinutes(30);
}
