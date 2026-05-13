namespace GameServer.Api.Services;

public static class BattlePassTelemetry
{
    public const string StateCreated = "battle_pass_state_created";
    public const string XpAdded = "battle_pass_xp_added";
    public const string LevelUp = "battle_pass_level_up";
    public const string RewardClaimed = "battle_pass_reward_claimed";
    public const string PurchaseStarted = "battle_pass_purchase_started";
    public const string PurchaseVerified = "battle_pass_purchase_verified";
    public const string PurchaseFailed = "battle_pass_purchase_failed";
    public const string EntitlementGranted = "battle_pass_entitlement_granted";
    public const string EntitlementRevoked = "battle_pass_entitlement_revoked";
    public const string SeasonStarted = "battle_pass_season_started";
    public const string SeasonEnded = "battle_pass_season_ended";
}
