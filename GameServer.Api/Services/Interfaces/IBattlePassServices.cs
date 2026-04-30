using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Models.Dto.Iap;

namespace GameServer.Api.Services.Interfaces;

public interface IBattlePassService
{
    Task<BattlePassCurrentResponse> GetCurrentAsync(string playerId, CancellationToken ct = default);
    Task<BattlePassScheduleResponse> GetScheduleAsync(CancellationToken ct = default);
    Task<BattlePassClaimResponse> ClaimAsync(ClaimBattlePassRewardRequest request, CancellationToken ct = default);
    Task<BattlePassClaimResponse> ClaimAllAsync(ClaimAllBattlePassRewardsRequest request, CancellationToken ct = default);
    Task<BattlePassStateDto> GetSeasonStateAsync(string playerId, string seasonId, CancellationToken ct = default);
    Task<BattlePassStateDto> AddXpToCurrentSeasonAsync(string playerId, int amount, CancellationToken ct = default);
    Task<BattlePassStateDto> AddXpAsync(string playerId, string seasonId, int amount, string source, CancellationToken ct = default);
}

public interface IIapService
{
    Task<VerifyGooglePurchaseResponse> VerifyGooglePurchaseAsync(VerifyGooglePurchaseRequest request, CancellationToken ct = default);
}

public interface IBattlePassAdminService
{
    Task<BattlePassAdminOperationResponse> CreateSeasonAsync(CreateBattlePassSeasonRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> UpdateSeasonAsync(string seasonId, UpdateBattlePassSeasonRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> UpsertLevelsAsync(string seasonId, UpsertBattlePassLevelsRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> PublishSeasonAsync(string seasonId, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> ArchiveSeasonAsync(string seasonId, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> RecreateSeasonAsync(RecreateBattlePassSeasonRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> ResetPlayerProgressAsync(ResetBattlePassPlayerRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> GrantEntitlementAsync(AdminGrantEntitlementRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> RevokeEntitlementAsync(AdminRevokeEntitlementRequest request, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> GetPlayerStateAsync(string playerId, string? seasonId, CancellationToken ct = default);
    Task<BattlePassAdminOperationResponse> GetPurchaseAsync(string purchaseId, CancellationToken ct = default);
}

public interface IEntitlementService
{
    Task<string> GetBattlePassPassTypeAsync(string playerId, string seasonId, long nowUtcMs, CancellationToken ct = default);
    Task<UserEntitlementRecord?> GetActiveBattlePassEntitlementAsync(string playerId, string seasonId, long nowUtcMs, CancellationToken ct = default);
    Task<IReadOnlyList<UserEntitlementRecord>> GetPlayerEntitlementsAsync(string playerId, CancellationToken ct = default);
    Task<EntitlementMutationResult> GrantBattlePassEntitlementAsync(
        string playerId,
        string seasonId,
        string passType,
        string sourceType,
        string? sourcePurchaseId,
        CancellationToken ct = default);
    Task<EntitlementMutationResult> RevokeBattlePassEntitlementAsync(
        string playerId,
        string seasonId,
        string? passType,
        CancellationToken ct = default);
}

public interface IGooglePlayPurchaseVerifier
{
    Task<GooglePlayPurchaseVerificationResult> VerifyAsync(string productId, string purchaseToken, CancellationToken ct = default);
    Task<GooglePlayAcknowledgeResult> AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct = default);
}

public interface IGooglePlayProductPurchaseClient
{
    Task<GooglePlayProductPurchasePayload> GetAsync(string packageName, string productId, string purchaseToken, CancellationToken ct = default);
    Task<string> AcknowledgeAsync(string packageName, string productId, string purchaseToken, CancellationToken ct = default);
}

public interface ILiveOpsScheduleItemProvider
{
    Task<IReadOnlyList<LiveOpsScheduleItem>> GetItemsAsync(CancellationToken ct = default);
}

public sealed class EntitlementMutationResult
{
    public bool Success { get; init; }
    public UserEntitlementRecord? Entitlement { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GooglePlayPurchaseVerificationResult
{
    public bool Success { get; init; }
    public string ProductId { get; init; } = string.Empty;
    public string PurchaseState { get; init; } = string.Empty;
    public bool IsAcknowledged { get; init; }
    public string ProviderPayloadJson { get; init; } = "{}";
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GooglePlayAcknowledgeResult
{
    public bool Success { get; init; }
    public string ProviderPayloadJson { get; init; } = "{}";
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GooglePlayProductPurchasePayload
{
    public string ProductId { get; init; } = string.Empty;
    public int PurchaseState { get; init; }
    public int AcknowledgementState { get; init; }
    public string ProviderPayloadJson { get; init; } = "{}";
}
