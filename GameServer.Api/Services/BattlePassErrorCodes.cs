namespace GameServer.Api.Services;

public static class BattlePassErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";
    public const string NoActiveSeason = "NO_ACTIVE_SEASON";
    public const string SeasonNotFound = "SEASON_NOT_FOUND";
    public const string SeasonNotActive = "SEASON_NOT_ACTIVE";
    public const string SeasonNotPurchasable = "SEASON_NOT_PURCHASABLE";
    public const string RewardAlreadyClaimed = "REWARD_ALREADY_CLAIMED";
    public const string RewardTrackNotAllowed = "REWARD_TRACK_NOT_ALLOWED";
    public const string LevelNotReached = "LEVEL_NOT_REACHED";
    public const string SeasonNotEditable = "SEASON_NOT_EDITABLE";
    public const string SeasonValidationFailed = "SEASON_VALIDATION_FAILED";
    public const string LevelsNotConfigured = "LEVELS_NOT_CONFIGURED";
    public const string RewardDefinitionMissing = "REWARD_DEFINITION_MISSING";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string PurchaseAlreadyUsed = "PURCHASE_ALREADY_USED";
    public const string PurchaseProductMismatch = "PURCHASE_PRODUCT_MISMATCH";
    public const string PurchaseNotPurchased = "PURCHASE_NOT_PURCHASED";
    public const string PurchaseRevoked = "PURCHASE_REVOKED";
    public const string PassAlreadyOwned = "PASS_ALREADY_OWNED";
    public const string PurchaseVerifyFailed = "PURCHASE_VERIFY_FAILED";
    public const string PurchaseAcknowledgeFailed = "PURCHASE_ACKNOWLEDGE_FAILED";
    public const string PurchaseNotFound = "PURCHASE_NOT_FOUND";
}
