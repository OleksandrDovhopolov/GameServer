namespace GameServer.Api.Services;

public static class RewardIntentErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";
    public const string RewardNotSupported = "REWARD_NOT_SUPPORTED";
    public const string IntentCreateFailed = "INTENT_CREATE_FAILED";
    public const string IntentNotFound = "INTENT_NOT_FOUND";
    public const string IntentAlreadyFulfilled = "INTENT_ALREADY_FULFILLED";
    public const string IntentExpired = "INTENT_EXPIRED";
    public const string CallbackDuplicate = "CALLBACK_DUPLICATE";
    public const string CallbackInvalid = "CALLBACK_INVALID";
    public const string RewardGrantFailed = "REWARD_GRANT_FAILED";
}
