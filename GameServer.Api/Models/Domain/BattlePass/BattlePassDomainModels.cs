namespace GameServer.Api.Models.Domain.BattlePass;

public static class BattlePassSeasonStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Active = "active";
    public const string Ended = "ended";
    public const string Archived = "archived";
}

public static class BattlePassPassTypes
{
    public const string None = "none";
    public const string Gold = "gold";
    public const string Platinum = "platinum";
}

public static class BattlePassRewardTracks
{
    public const string Default = "default";
    public const string Premium = "premium";
    public const string Platinum = "platinum";
}

public static class EntitlementTypes
{
    public const string BattlePass = "battle_pass";
}

public static class EntitlementSourceTypes
{
    public const string Purchase = "purchase";
    public const string Grant = "grant";
    public const string Admin = "admin";
    public const string Event = "event";
    public const string Compensation = "compensation";
}

public static class EntitlementStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class PurchasePlatforms
{
    public const string GooglePlay = "google_play";
}

public static class PurchaseTypes
{
    public const string BattlePass = "battle_pass";
    public const string ShopItem = "shop_item";
    public const string CurrencyPack = "currency_pack";
    public const string Subscription = "subscription";
}

public static class PurchaseStatuses
{
    public const string Pending = "pending";
    public const string Verified = "verified";
    public const string Granted = "granted";
    public const string Acknowledged = "acknowledged";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string Revoked = "revoked";
}

public sealed class BattlePassSeasonRecord
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public long StartAtUtcMs { get; init; }
    public long EndAtUtcMs { get; init; }
    public int MaxLevel { get; init; }
    public string Status { get; init; } = BattlePassSeasonStatuses.Draft;
    public string ConfigVersion { get; init; } = "v1";
    public string GoldProductId { get; init; } = string.Empty;
    public string PlatinumProductId { get; init; } = string.Empty;
    public long CreatedAtUtcMs { get; init; }
    public long UpdatedAtUtcMs { get; init; }
}

public sealed class BattlePassLevelRecord
{
    public string SeasonId { get; init; } = string.Empty;
    public int Level { get; init; }
    public int XpRequired { get; init; }
    public List<string> DefaultRewardIds { get; init; } = [];
    public List<string> PremiumRewardIds { get; init; } = [];
    public List<string> PlatinumRewardIds { get; init; } = [];
}

public sealed class ClaimedBattlePassRewardRecord
{
    public string Key { get; init; } = string.Empty;
    public int Level { get; init; }
    public string RewardTrack { get; init; } = BattlePassRewardTracks.Default;
    public long ClaimedAtUtcMs { get; init; }
}

public sealed class UserBattlePassRecord
{
    public string PlayerId { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
    public int Xp { get; init; }
    public int Level { get; init; }
    public List<ClaimedBattlePassRewardRecord> ClaimedRewards { get; init; } = [];
    public long CreatedAtUtcMs { get; init; }
    public long UpdatedAtUtcMs { get; init; }
}

public sealed class UserEntitlementRecord
{
    public string Id { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string EntitlementType { get; init; } = string.Empty;
    public string EntitlementKey { get; init; } = string.Empty;
    public string? SourcePurchaseId { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string Status { get; init; } = EntitlementStatuses.Active;
    public long StartsAtUtcMs { get; init; }
    public long? ExpiresAtUtcMs { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
    public long CreatedAtUtcMs { get; init; }
    public long UpdatedAtUtcMs { get; init; }
}

public sealed class PurchaseRecord
{
    public string Id { get; init; } = string.Empty;
    public string PlayerId { get; init; } = string.Empty;
    public string Platform { get; init; } = PurchasePlatforms.GooglePlay;
    public string ProductId { get; init; } = string.Empty;
    public string PurchaseTokenHash { get; init; } = string.Empty;
    public string Status { get; init; } = PurchaseStatuses.Pending;
    public string PurchaseType { get; init; } = string.Empty;
    public string? LinkedEntitlementId { get; init; }
    public string ProviderPayloadJson { get; init; } = "{}";
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long CreatedAtUtcMs { get; init; }
    public long UpdatedAtUtcMs { get; init; }
}

public sealed class CatalogProductRecord
{
    public string ProductId { get; init; } = string.Empty;
    public string Platform { get; init; } = PurchasePlatforms.GooglePlay;
    public string PurchaseType { get; init; } = string.Empty;
    public string EntitlementType { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
    public long CreatedAtUtcMs { get; init; }
    public long UpdatedAtUtcMs { get; init; }
}

public sealed class LiveOpsScheduleItem
{
    public string Id { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string StreamId { get; init; } = string.Empty;
    public long StartTimeUtcMs { get; init; }
    public long EndTimeUtcMs { get; init; }
    public Dictionary<string, string> CustomParams { get; init; } = new(StringComparer.Ordinal);
}
