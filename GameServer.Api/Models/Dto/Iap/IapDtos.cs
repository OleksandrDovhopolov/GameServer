using GameServer.Api.Models.Dto.BattlePass;

namespace GameServer.Api.Models.Dto.Iap;

public static class VerifyGooglePurchaseStatuses
{
    public const string VerifiedAndConsumed = "verified_and_consumed";
    public const string VerifiedAndAcknowledged = "verified_and_acknowledged";
    public const string DuplicateAlreadyProcessed = "duplicate_already_processed";
    public const string GrantedButFinalizationPending = "granted_but_finalization_pending";
    public const string VerifyFailed = "verify_failed";
}

public static class GooglePlayFinalizeStatuses
{
    public const string Consumed = "consumed";
    public const string Acknowledged = "acknowledged";
    public const string FinalizationPendingManual = "finalization_pending_manual";
    public const string NotAttempted = "not_attempted";
    public const string NotApplicable = "not_applicable";
}

public sealed class VerifyGooglePurchaseRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string PurchaseToken { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
}

public sealed class PurchaseEntitlementDto
{
    public string Type { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class VerifyGooglePurchaseResponse
{
    public bool Success { get; init; }
    public string PurchaseStatus { get; init; } = string.Empty;
    public string GoogleFinalizeStatus { get; init; } = GooglePlayFinalizeStatuses.NotApplicable;
    public PurchaseEntitlementDto? Entitlement { get; init; }
    public BattlePassStateDto? BattlePass { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
