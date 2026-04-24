using GameServer.Api.Models.Dto.BattlePass;

namespace GameServer.Api.Models.Dto.Iap;

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
    public PurchaseEntitlementDto? Entitlement { get; init; }
    public BattlePassStateDto? BattlePass { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
