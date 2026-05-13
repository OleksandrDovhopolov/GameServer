namespace GameServer.Api.Services;

public static class RewardErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string PlayerNotFound = "player_not_found";
    public const string RewardNotFound = "reward_not_found";
    public const string UnsupportedRewardKind = "unsupported_reward_kind";
    public const string RewardApplicationFailed = "reward_application_failed";
}
