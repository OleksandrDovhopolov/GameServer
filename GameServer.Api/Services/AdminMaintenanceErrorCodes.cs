namespace GameServer.Api.Services;

public static class AdminMaintenanceErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string FeatureDisabled = "FEATURE_DISABLED";
    public const string RedisFlushFailed = "REDIS_FLUSH_FAILED";
    public const string PlayerSaveResetFailed = "PLAYER_SAVE_RESET_FAILED";
}
