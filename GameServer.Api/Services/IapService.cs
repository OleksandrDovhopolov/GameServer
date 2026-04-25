using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class IapService : IIapService
{
    private readonly IPlayerEconomyRepository _playerEconomyRepository;
    private readonly IProductCatalogRepository _productCatalogRepository;
    private readonly IPurchaseRepository _purchaseRepository;
    private readonly IBattlePassSeasonRepository _seasonRepository;
    private readonly IEntitlementService _entitlementService;
    private readonly IGooglePlayPurchaseVerifier _googlePlayPurchaseVerifier;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IBattlePassService _battlePassService;
    private readonly ISystemClock _clock;
    private readonly ILogger<IapService> _logger;

    public IapService(
        IPlayerEconomyRepository playerEconomyRepository,
        IProductCatalogRepository productCatalogRepository,
        IPurchaseRepository purchaseRepository,
        IBattlePassSeasonRepository seasonRepository,
        IEntitlementService entitlementService,
        IGooglePlayPurchaseVerifier googlePlayPurchaseVerifier,
        IDistributedLockService distributedLockService,
        IBattlePassService battlePassService,
        ISystemClock clock,
        ILogger<IapService> logger)
    {
        _playerEconomyRepository = playerEconomyRepository;
        _productCatalogRepository = productCatalogRepository;
        _purchaseRepository = purchaseRepository;
        _seasonRepository = seasonRepository;
        _entitlementService = entitlementService;
        _googlePlayPurchaseVerifier = googlePlayPurchaseVerifier;
        _distributedLockService = distributedLockService;
        _battlePassService = battlePassService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<VerifyGooglePurchaseResponse> VerifyGooglePurchaseAsync(VerifyGooglePurchaseRequest request, CancellationToken ct = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.PlayerId) ||
            string.IsNullOrWhiteSpace(request.ProductId) ||
            string.IsNullOrWhiteSpace(request.PurchaseToken) ||
            string.IsNullOrWhiteSpace(request.SeasonId))
        {
            return BuildFailure("playerId, productId, purchaseToken and seasonId are required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(request.PlayerId, ct);
        if (!playerExists)
        {
            return BuildFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
        }

        var purchaseTokenHash = PurchaseTokenHasher.Hash(request.PurchaseToken);
        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["PlayerId"] = request.PlayerId,
            ["ProductId"] = request.ProductId,
            ["SeasonId"] = request.SeasonId,
            ["PurchaseTokenHash"] = purchaseTokenHash
        });

        await using var purchaseLock = await _distributedLockService.AcquireAsync(
            $"purchase:{purchaseTokenHash}",
            TimeSpan.FromSeconds(20),
            ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var season = await _seasonRepository.GetByIdAsync(request.SeasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        var normalizedSeason = BattlePassSeasonState.Normalize(season, nowUtcMs);
        if (!BattlePassSeasonState.IsPurchasable(normalizedSeason.Status))
        {
            return BuildFailure("Season is not available for purchase.", BattlePassErrorCodes.SeasonNotPurchasable);
        }

        var product = await _productCatalogRepository.GetByIdAsync(request.ProductId, ct);
        if (product is null)
        {
            return BuildFailure("Product was not found.", BattlePassErrorCodes.ProductNotFound);
        }

        if (!product.Metadata.TryGetValue("seasonId", out var catalogSeasonId) ||
            !string.Equals(catalogSeasonId, request.SeasonId, StringComparison.Ordinal))
        {
            return BuildFailure("Product does not belong to the requested season.", BattlePassErrorCodes.ProductNotFound);
        }

        var requestedPassType = product.Metadata.TryGetValue("passType", out var productPassType)
            ? productPassType
            : BattlePassPassTypes.None;
        if (string.Equals(requestedPassType, BattlePassPassTypes.None, StringComparison.Ordinal))
        {
            return BuildFailure("Product pass type is not configured.", BattlePassErrorCodes.ProductNotFound);
        }

        var existingPurchase = await _purchaseRepository.GetByTokenHashAsync(purchaseTokenHash, ct);
        if (existingPurchase is not null && !string.Equals(existingPurchase.PlayerId, request.PlayerId, StringComparison.Ordinal))
        {
            return BuildFailure("Purchase token is already linked to another player.", BattlePassErrorCodes.PurchaseAlreadyUsed);
        }

        if (existingPurchase is not null && string.Equals(existingPurchase.Status, PurchaseStatuses.Acknowledged, StringComparison.Ordinal))
        {
            return await BuildSuccessResponseAsync(
                existingPurchase.Status,
                request.PlayerId,
                request.SeasonId,
                await _entitlementService.GetActiveBattlePassEntitlementAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct),
                ct);
        }

        _logger.LogInformation(
            "{EventName} ProductId: {ProductId}, SeasonId: {SeasonId}, ExistingPurchaseId: {ExistingPurchaseId}",
            BattlePassTelemetry.PurchaseStarted,
            request.ProductId,
            request.SeasonId,
            existingPurchase?.Id ?? string.Empty);

        var verification = await _googlePlayPurchaseVerifier.VerifyAsync(request.ProductId, request.PurchaseToken, ct);
        if (!verification.Success)
        {
            var failed = await SavePurchaseAsync(
                existingPurchase,
                request.PlayerId,
                purchaseTokenHash,
                verification.ProductId,
                request.ProductId,
                product.PurchaseType,
                PurchaseStatuses.Failed,
                verification.ProviderPayloadJson,
                verification.ErrorCode ?? BattlePassErrorCodes.PurchaseVerifyFailed,
                verification.ErrorMessage ?? "Purchase verification failed.",
                ct);
            await TryEnsureTokenIndexAsync(existingPurchase, failed, purchaseTokenHash, ct);
            _logger.LogWarning(
                "{EventName} PurchaseId: {PurchaseId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}, PurchaseTokenHash: {PurchaseTokenHash}",
                BattlePassTelemetry.PurchaseFailed,
                failed.Id,
                failed.ErrorCode,
                failed.ErrorMessage,
                purchaseTokenHash);
            return BuildFailure(failed.ErrorMessage ?? "Purchase verification failed.", failed.ErrorCode ?? BattlePassErrorCodes.PurchaseVerifyFailed, failed.Status);
        }

        if (!string.Equals(verification.ProductId, request.ProductId, StringComparison.Ordinal))
        {
            var mismatch = await SavePurchaseAsync(
                existingPurchase,
                request.PlayerId,
                purchaseTokenHash,
                verification.ProductId,
                request.ProductId,
                product.PurchaseType,
                PurchaseStatuses.Failed,
                verification.ProviderPayloadJson,
                BattlePassErrorCodes.PurchaseProductMismatch,
                "Provider returned another product id.",
                ct);
            await TryEnsureTokenIndexAsync(existingPurchase, mismatch, purchaseTokenHash, ct);
            return BuildFailure("Provider returned another product id.", BattlePassErrorCodes.PurchaseProductMismatch, mismatch.Status);
        }

        var verificationStatus = verification.PurchaseState switch
        {
            "pending" => PurchaseStatuses.Pending,
            "refunded" => PurchaseStatuses.Refunded,
            "revoked" => PurchaseStatuses.Revoked,
            _ => PurchaseStatuses.Verified
        };
        var purchase = await SavePurchaseAsync(
            existingPurchase,
            request.PlayerId,
            purchaseTokenHash,
            request.ProductId,
            request.ProductId,
            product.PurchaseType,
            verificationStatus,
            verification.ProviderPayloadJson,
            verificationStatus == PurchaseStatuses.Verified ? null : BattlePassErrorCodes.PurchaseNotPurchased,
            verificationStatus == PurchaseStatuses.Verified ? null : $"Provider purchase state is '{verification.PurchaseState}'.",
            ct);
        await TryEnsureTokenIndexAsync(existingPurchase, purchase, purchaseTokenHash, ct);

        if (!string.Equals(verificationStatus, PurchaseStatuses.Verified, StringComparison.Ordinal))
        {
            var errorCode = verificationStatus switch
            {
                PurchaseStatuses.Refunded => BattlePassErrorCodes.PurchaseRevoked,
                PurchaseStatuses.Revoked => BattlePassErrorCodes.PurchaseRevoked,
                _ => BattlePassErrorCodes.PurchaseNotPurchased
            };
            _logger.LogWarning(
                "{EventName} PurchaseId: {PurchaseId}, ProviderState: {ProviderState}, PurchaseTokenHash: {PurchaseTokenHash}",
                BattlePassTelemetry.PurchaseFailed,
                purchase.Id,
                verification.PurchaseState,
                purchaseTokenHash);
            return BuildFailure(purchase.ErrorMessage ?? "Purchase is not in purchased state.", errorCode, purchase.Status);
        }

        var currentPassType = await _entitlementService.GetBattlePassPassTypeAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
        if (!CanGrantPass(currentPassType, requestedPassType))
        {
            return BuildFailure("Player already owns the same or better pass.", BattlePassErrorCodes.PassAlreadyOwned, purchase.Status);
        }

        _logger.LogInformation(
            "{EventName} PurchaseId: {PurchaseId}, PurchaseTokenHash: {PurchaseTokenHash}, ProductId: {ProductId}",
            BattlePassTelemetry.PurchaseVerified,
            purchase.Id,
            purchaseTokenHash,
            request.ProductId);

        await using var battlePassLock = await _distributedLockService.AcquireAsync(
            $"bp:{request.PlayerId}:{request.SeasonId}",
            TimeSpan.FromSeconds(20),
            ct);

        var entitlementResult = await _entitlementService.GrantBattlePassEntitlementAsync(
            request.PlayerId,
            request.SeasonId,
            requestedPassType,
            EntitlementSourceTypes.Purchase,
            purchase.Id,
            ct);
        if (!entitlementResult.Success || entitlementResult.Entitlement is null)
        {
            var failedPurchase = await SavePurchaseAsync(
                purchase,
                request.PlayerId,
                purchaseTokenHash,
                request.ProductId,
                request.ProductId,
                product.PurchaseType,
                PurchaseStatuses.Failed,
                verification.ProviderPayloadJson,
                entitlementResult.ErrorCode ?? BattlePassErrorCodes.PassAlreadyOwned,
                entitlementResult.ErrorMessage ?? "Entitlement grant failed.",
                ct);
            return BuildFailure(
                failedPurchase.ErrorMessage ?? "Entitlement grant failed.",
                failedPurchase.ErrorCode ?? BattlePassErrorCodes.PassAlreadyOwned,
                failedPurchase.Status);
        }

        purchase = await SavePurchaseAsync(
            purchase,
            request.PlayerId,
            purchaseTokenHash,
            request.ProductId,
            request.ProductId,
            product.PurchaseType,
            verification.IsAcknowledged ? PurchaseStatuses.Acknowledged : PurchaseStatuses.Granted,
            verification.ProviderPayloadJson,
            null,
            null,
            ct,
            entitlementResult.Entitlement.Id);

        if (!verification.IsAcknowledged)
        {
            var acknowledge = await _googlePlayPurchaseVerifier.AcknowledgeAsync(request.ProductId, request.PurchaseToken, ct);
            if (!acknowledge.Success)
            {
                var grantedPurchase = await SavePurchaseAsync(
                    purchase,
                    request.PlayerId,
                    purchaseTokenHash,
                    request.ProductId,
                    request.ProductId,
                    product.PurchaseType,
                    PurchaseStatuses.Granted,
                    acknowledge.ProviderPayloadJson,
                    acknowledge.ErrorCode ?? BattlePassErrorCodes.PurchaseAcknowledgeFailed,
                    acknowledge.ErrorMessage ?? "Purchase acknowledge failed.",
                    ct,
                    entitlementResult.Entitlement.Id);
                _logger.LogWarning(
                    "{EventName} PurchaseId: {PurchaseId}, ErrorCode: {ErrorCode}, PurchaseTokenHash: {PurchaseTokenHash}",
                    BattlePassTelemetry.PurchaseFailed,
                    grantedPurchase.Id,
                    grantedPurchase.ErrorCode,
                    purchaseTokenHash);
                return new VerifyGooglePurchaseResponse
                {
                    Success = false,
                    PurchaseStatus = grantedPurchase.Status,
                    Entitlement = new PurchaseEntitlementDto
                    {
                        Type = entitlementResult.Entitlement.EntitlementType,
                        Key = entitlementResult.Entitlement.EntitlementKey,
                        Status = entitlementResult.Entitlement.Status
                    },
                    BattlePass = await _battlePassService.GetSeasonStateAsync(request.PlayerId, request.SeasonId, ct),
                    ErrorCode = grantedPurchase.ErrorCode,
                    ErrorMessage = grantedPurchase.ErrorMessage
                };
            }

            purchase = await SavePurchaseAsync(
                purchase,
                request.PlayerId,
                purchaseTokenHash,
                request.ProductId,
                request.ProductId,
                product.PurchaseType,
                PurchaseStatuses.Acknowledged,
                acknowledge.ProviderPayloadJson,
                null,
                null,
                ct,
                entitlementResult.Entitlement.Id);
        }

        return await BuildSuccessResponseAsync(
            purchase.Status,
            request.PlayerId,
            request.SeasonId,
            entitlementResult.Entitlement,
            ct);
    }

    private async Task<PurchaseRecord> SavePurchaseAsync(
        PurchaseRecord? existingPurchase,
        string playerId,
        string purchaseTokenHash,
        string providerProductId,
        string fallbackProductId,
        string purchaseType,
        string status,
        string providerPayloadJson,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct,
        string? linkedEntitlementId = null)
    {
        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var purchase = new PurchaseRecord
        {
            Id = existingPurchase?.Id ?? $"pur_{Guid.NewGuid():N}",
            PlayerId = existingPurchase?.PlayerId ?? playerId,
            Platform = PurchasePlatforms.GooglePlay,
            ProductId = string.IsNullOrWhiteSpace(providerProductId) ? fallbackProductId : providerProductId,
            PurchaseTokenHash = purchaseTokenHash,
            Status = status,
            PurchaseType = existingPurchase?.PurchaseType ?? purchaseType,
            LinkedEntitlementId = linkedEntitlementId ?? existingPurchase?.LinkedEntitlementId,
            ProviderPayloadJson = string.IsNullOrWhiteSpace(providerPayloadJson) ? existingPurchase?.ProviderPayloadJson ?? "{}" : providerPayloadJson,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CreatedAtUtcMs = existingPurchase?.CreatedAtUtcMs ?? nowUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        };

        await _purchaseRepository.SaveAsync(purchase, ct);
        return purchase;
    }

    private async Task TryEnsureTokenIndexAsync(
        PurchaseRecord? existingPurchase,
        PurchaseRecord purchase,
        string purchaseTokenHash,
        CancellationToken ct)
    {
        if (existingPurchase is not null)
        {
            return;
        }

        var indexed = await _purchaseRepository.TryCreateTokenIndexAsync(purchaseTokenHash, purchase.Id, ct);
        if (!indexed)
        {
            var indexedPurchase = await _purchaseRepository.GetByTokenHashAsync(purchaseTokenHash, ct);
            if (indexedPurchase is not null &&
                !string.Equals(indexedPurchase.PlayerId, purchase.PlayerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Purchase token was concurrently linked to another player.");
            }
        }
    }

    private async Task<VerifyGooglePurchaseResponse> BuildSuccessResponseAsync(
        string purchaseStatus,
        string playerId,
        string seasonId,
        UserEntitlementRecord? entitlement,
        CancellationToken ct)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = true,
            PurchaseStatus = purchaseStatus,
            Entitlement = entitlement is null
                ? null
                : new PurchaseEntitlementDto
                {
                    Type = entitlement.EntitlementType,
                    Key = entitlement.EntitlementKey,
                    Status = entitlement.Status
                },
            BattlePass = await _battlePassService.GetSeasonStateAsync(playerId, seasonId, ct)
        };
    }

    private static bool CanGrantPass(string currentPassType, string requestedPassType)
    {
        if (string.Equals(currentPassType, BattlePassPassTypes.None, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(currentPassType, BattlePassPassTypes.Premium, StringComparison.Ordinal))
        {
            return string.Equals(requestedPassType, BattlePassPassTypes.Platinum, StringComparison.Ordinal);
        }

        return false;
    }

    private static VerifyGooglePurchaseResponse BuildFailure(
        string errorMessage,
        string errorCode = BattlePassErrorCodes.ValidationError,
        string purchaseStatus = PurchaseStatuses.Failed)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = false,
            PurchaseStatus = purchaseStatus,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
