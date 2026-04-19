namespace GameServer.Api.Services;

public static class InventoryErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string PlayerNotFound = "player_not_found";
    public const string InsufficientInventory = "insufficient_inventory";
    public const string InventoryAdjustmentFailed = "inventory_adjustment_failed";
}
