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
            return BuildVerifyFailure("playerId, productId, purchaseToken and seasonId are required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(request.PlayerId, ct);
        if (!playerExists)
        {
            return BuildVerifyFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
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
            return BuildVerifyFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        var normalizedSeason = BattlePassSeasonState.Normalize(season, nowUtcMs);
        if (!BattlePassSeasonState.IsPurchasable(normalizedSeason.Status))
        {
            return BuildVerifyFailure("Season is not available for purchase.", BattlePassErrorCodes.SeasonNotPurchasable);
        }

        var product = await _productCatalogRepository.GetByIdAsync(request.ProductId, ct);
        if (product is null)
        {
            return BuildVerifyFailure("Product was not found.", BattlePassErrorCodes.ProductNotFound);
        }

        if (!product.Metadata.TryGetValue(CatalogProductMetadataKeys.SeasonId, out var catalogSeasonId) ||
            !string.Equals(catalogSeasonId, request.SeasonId, StringComparison.Ordinal))
        {
            return BuildVerifyFailure("Product does not belong to the requested season.", BattlePassErrorCodes.ProductNotFound);
        }

        var requestedPassType = product.Metadata.TryGetValue(CatalogProductMetadataKeys.PassType, out var productPassType)
            ? productPassType
            : BattlePassPassTypes.None;
        if (string.Equals(requestedPassType, BattlePassPassTypes.None, StringComparison.Ordinal))
        {
            return BuildVerifyFailure("Product pass type is not configured.", BattlePassErrorCodes.ProductNotFound);
        }

        if (!product.Metadata.TryGetValue(CatalogProductMetadataKeys.FinalizationMode, out var finalizationMode) ||
            !IsSupportedFinalizationMode(finalizationMode))
        {
            return BuildVerifyFailure("Product finalization mode is not configured.", BattlePassErrorCodes.ValidationError);
        }

        var existingPurchase = await _purchaseRepository.GetByTokenHashAsync(purchaseTokenHash, ct);
        if (existingPurchase is not null &&
            !string.Equals(existingPurchase.PlayerId, request.PlayerId, StringComparison.Ordinal))
        {
            return BuildVerifyFailure("Purchase token is already linked to another player.", BattlePassErrorCodes.PurchaseAlreadyUsed);
        }

        if (existingPurchase is not null && IsPurchaseFinalized(existingPurchase, finalizationMode))
        {
            var entitlement = await _entitlementService.GetActiveBattlePassEntitlementAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
            if (entitlement is null)
            {
                return BuildVerifyFailure(
                    "Purchase is already finalized but no active entitlement was found.",
                    BattlePassErrorCodes.PurchaseAlreadyProcessed);
            }

            return await BuildSuccessResponseAsync(
                request.PlayerId,
                request.SeasonId,
                entitlement,
                GetSuccessPurchaseStatus(existingPurchase.Status),
                GetGoogleFinalizeStatus(existingPurchase.Status),
                isDuplicate: true,
                ct);
        }

        _logger.LogInformation(
            "{EventName} ProductId: {ProductId}, SeasonId: {SeasonId}, ExistingPurchaseId: {ExistingPurchaseId}, VerifierType: {VerifierType}, FinalizationMode: {FinalizationMode}",
            BattlePassTelemetry.PurchaseStarted,
            request.ProductId,
            request.SeasonId,
            existingPurchase?.Id ?? string.Empty,
            _googlePlayPurchaseVerifier.GetType().Name,
            finalizationMode);

        var verification = await _googlePlayPurchaseVerifier.VerifyAsync(request.ProductId, request.PurchaseToken, ct);
        if (!verification.Success)
        {
            if (existingPurchase is not null && IsPendingFinalization(existingPurchase, finalizationMode))
            {
                var existingEntitlement = await _entitlementService.GetActiveBattlePassEntitlementAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
                if (existingEntitlement is null)
                {
                    return BuildVerifyFailure(
                        "Purchase finalization is pending but the linked entitlement is missing.",
                        BattlePassErrorCodes.PurchaseAlreadyProcessed);
                }

                var pendingPurchase = await SavePurchaseAsync(
                    existingPurchase,
                    request.PlayerId,
                    purchaseTokenHash,
                    request.ProductId,
                    request.ProductId,
                    product.PurchaseType,
                    PurchaseStatuses.GrantedFinalizationPending,
                    verification.ProviderPayloadJson,
                    verification.ErrorCode ?? BattlePassErrorCodes.PurchaseVerifyFailed,
                    verification.ErrorMessage ?? "Purchase verification failed while finalization is pending.",
                    existingPurchase.LinkedEntitlementId,
                    finalizationMode,
                    existingPurchase.FinalizationAttemptCount,
                    existingPurchase.LastFinalizationAttemptAtUtcMs,
                    existingPurchase.FinalizationErrorCode,
                    existingPurchase.FinalizationErrorMessage,
                    existingPurchase.FinalizedAtUtcMs,
                    ct);

                return await BuildPendingFinalizationResponseAsync(
                    pendingPurchase,
                    request.PlayerId,
                    request.SeasonId,
                    existingEntitlement,
                    ct);
            }

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
                existingPurchase?.LinkedEntitlementId,
                finalizationMode,
                existingPurchase?.FinalizationAttemptCount ?? 0,
                existingPurchase?.LastFinalizationAttemptAtUtcMs,
                existingPurchase?.FinalizationErrorCode,
                existingPurchase?.FinalizationErrorMessage,
                existingPurchase?.FinalizedAtUtcMs,
                ct);
            await TryEnsureTokenIndexAsync(existingPurchase, failed, purchaseTokenHash, ct);
            _logger.LogWarning(
                "{EventName} PurchaseId: {PurchaseId}, ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}, PurchaseTokenHash: {PurchaseTokenHash}",
                BattlePassTelemetry.PurchaseFailed,
                failed.Id,
                failed.ErrorCode,
                failed.ErrorMessage,
                purchaseTokenHash);
            return BuildVerifyFailure(
                failed.ErrorMessage ?? "Purchase verification failed.",
                failed.ErrorCode ?? BattlePassErrorCodes.PurchaseVerifyFailed);
        }

        if (!string.IsNullOrWhiteSpace(verification.ProductId) &&
            !string.Equals(verification.ProductId, request.ProductId, StringComparison.Ordinal))
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
                existingPurchase?.LinkedEntitlementId,
                finalizationMode,
                existingPurchase?.FinalizationAttemptCount ?? 0,
                existingPurchase?.LastFinalizationAttemptAtUtcMs,
                existingPurchase?.FinalizationErrorCode,
                existingPurchase?.FinalizationErrorMessage,
                existingPurchase?.FinalizedAtUtcMs,
                ct);
            await TryEnsureTokenIndexAsync(existingPurchase, mismatch, purchaseTokenHash, ct);
            _logger.LogWarning(
                "{EventName} RequestedProductId: {RequestedProductId}, ProviderProductId: {ProviderProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
                BattlePassTelemetry.PurchaseFailed,
                request.ProductId,
                verification.ProductId,
                purchaseTokenHash);
            return BuildVerifyFailure("Provider returned another product id.", BattlePassErrorCodes.PurchaseProductMismatch);
        }

        if (IsProviderFinalized(verification, finalizationMode))
        {
            if (existingPurchase is null || string.IsNullOrWhiteSpace(existingPurchase.LinkedEntitlementId))
            {
                return BuildVerifyFailure(
                    "Google Play reports the purchase is already finalized but the server has no trusted purchase record.",
                    BattlePassErrorCodes.PurchaseAlreadyProcessed);
            }

            var entitlement = await _entitlementService.GetActiveBattlePassEntitlementAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
            if (entitlement is null)
            {
                return BuildVerifyFailure(
                    "Google Play reports the purchase is already finalized but the entitlement is missing.",
                    BattlePassErrorCodes.PurchaseAlreadyProcessed);
            }

            var finalizedPurchase = await SavePurchaseAsync(
                existingPurchase,
                request.PlayerId,
                purchaseTokenHash,
                request.ProductId,
                request.ProductId,
                product.PurchaseType,
                GetFinalizedStatus(finalizationMode),
                verification.ProviderPayloadJson,
                null,
                null,
                existingPurchase.LinkedEntitlementId,
                finalizationMode,
                existingPurchase.FinalizationAttemptCount,
                existingPurchase.LastFinalizationAttemptAtUtcMs,
                null,
                null,
                existingPurchase.FinalizedAtUtcMs ?? nowUtcMs,
                ct);

            return await BuildSuccessResponseAsync(
                request.PlayerId,
                request.SeasonId,
                entitlement,
                GetSuccessPurchaseStatus(finalizedPurchase.Status),
                GetGoogleFinalizeStatus(finalizedPurchase.Status),
                isDuplicate: true,
                ct);
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
            existingPurchase?.LinkedEntitlementId,
            finalizationMode,
            existingPurchase?.FinalizationAttemptCount ?? 0,
            existingPurchase?.LastFinalizationAttemptAtUtcMs,
            existingPurchase?.FinalizationErrorCode,
            existingPurchase?.FinalizationErrorMessage,
            existingPurchase?.FinalizedAtUtcMs,
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
            return BuildVerifyFailure(purchase.ErrorMessage ?? "Purchase is not in purchased state.", errorCode);
        }

        var hasGrantedEntitlement = existingPurchase is not null &&
                                    !string.IsNullOrWhiteSpace(existingPurchase.LinkedEntitlementId) &&
                                    IsPendingFinalization(existingPurchase, finalizationMode);

        UserEntitlementRecord? entitlementRecord;
        if (hasGrantedEntitlement)
        {
            entitlementRecord = await _entitlementService.GetActiveBattlePassEntitlementAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
            if (entitlementRecord is null)
            {
                return BuildVerifyFailure(
                    "Purchase finalization is pending but the linked entitlement is missing.",
                    BattlePassErrorCodes.PurchaseAlreadyProcessed);
            }
        }
        else
        {
            var currentPassType = await _entitlementService.GetBattlePassPassTypeAsync(request.PlayerId, request.SeasonId, nowUtcMs, ct);
            if (!CanGrantPass(currentPassType, requestedPassType))
            {
                return BuildVerifyFailure("Player already owns the same or better pass.", BattlePassErrorCodes.PassAlreadyOwned);
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
                    purchase.LinkedEntitlementId,
                    finalizationMode,
                    purchase.FinalizationAttemptCount,
                    purchase.LastFinalizationAttemptAtUtcMs,
                    purchase.FinalizationErrorCode,
                    purchase.FinalizationErrorMessage,
                    purchase.FinalizedAtUtcMs,
                    ct);
                return BuildVerifyFailure(
                    failedPurchase.ErrorMessage ?? "Entitlement grant failed.",
                    failedPurchase.ErrorCode ?? BattlePassErrorCodes.PassAlreadyOwned);
            }

            entitlementRecord = entitlementResult.Entitlement;
        }

        purchase = await SavePurchaseAsync(
            purchase,
            request.PlayerId,
            purchaseTokenHash,
            request.ProductId,
            request.ProductId,
            product.PurchaseType,
            PurchaseStatuses.GrantedFinalizationPending,
            verification.ProviderPayloadJson,
            null,
            null,
            entitlementRecord.Id,
            finalizationMode,
            purchase.FinalizationAttemptCount,
            purchase.LastFinalizationAttemptAtUtcMs,
            purchase.FinalizationErrorCode,
            purchase.FinalizationErrorMessage,
            purchase.FinalizedAtUtcMs,
            ct);

        return await FinalizePurchaseAsync(
            purchase,
            request.PlayerId,
            request.SeasonId,
            request.PurchaseToken,
            entitlementRecord,
            ct);
    }

    private async Task<VerifyGooglePurchaseResponse> FinalizePurchaseAsync(
        PurchaseRecord purchase,
        string playerId,
        string seasonId,
        string purchaseToken,
        UserEntitlementRecord entitlement,
        CancellationToken ct)
    {
        var finalizationAttemptAtUtcMs = _clock.UtcNowUnixMilliseconds;
        var finalizationAttemptCount = purchase.FinalizationAttemptCount + 1;

        if (string.Equals(purchase.FinalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal))
        {
            var consume = await _googlePlayPurchaseVerifier.ConsumeAsync(purchase.ProductId, purchaseToken, ct);
            if (!consume.Success)
            {
                var pendingPurchase = await SavePurchaseAsync(
                    purchase,
                    playerId,
                    purchase.PurchaseTokenHash,
                    purchase.ProductId,
                    purchase.ProductId,
                    purchase.PurchaseType,
                    PurchaseStatuses.GrantedFinalizationPending,
                    consume.ProviderPayloadJson,
                    consume.ErrorCode ?? BattlePassErrorCodes.PurchaseConsumeFailed,
                    consume.ErrorMessage ?? "Purchase consume failed.",
                    entitlement.Id,
                    purchase.FinalizationMode,
                    finalizationAttemptCount,
                    finalizationAttemptAtUtcMs,
                    consume.ErrorCode ?? BattlePassErrorCodes.PurchaseConsumeFailed,
                    consume.ErrorMessage ?? "Purchase consume failed.",
                    null,
                    ct);
                _logger.LogWarning(
                    "{EventName} PurchaseId: {PurchaseId}, ErrorCode: {ErrorCode}, PurchaseTokenHash: {PurchaseTokenHash}",
                    BattlePassTelemetry.PurchaseFailed,
                    pendingPurchase.Id,
                    pendingPurchase.ErrorCode,
                    purchase.PurchaseTokenHash);
                return await BuildPendingFinalizationResponseAsync(
                    pendingPurchase,
                    playerId,
                    seasonId,
                    entitlement,
                    ct);
            }

            var consumedPurchase = await SavePurchaseAsync(
                purchase,
                playerId,
                purchase.PurchaseTokenHash,
                purchase.ProductId,
                purchase.ProductId,
                purchase.PurchaseType,
                PurchaseStatuses.Consumed,
                consume.ProviderPayloadJson,
                null,
                null,
                entitlement.Id,
                purchase.FinalizationMode,
                finalizationAttemptCount,
                finalizationAttemptAtUtcMs,
                null,
                null,
                finalizationAttemptAtUtcMs,
                ct);
            return await BuildSuccessResponseAsync(
                playerId,
                seasonId,
                entitlement,
                VerifyGooglePurchaseStatuses.VerifiedAndConsumed,
                GooglePlayFinalizeStatuses.Consumed,
                isDuplicate: false,
                ct);
        }

        var acknowledge = await _googlePlayPurchaseVerifier.AcknowledgeAsync(purchase.ProductId, purchaseToken, ct);
        if (!acknowledge.Success)
        {
            var pendingPurchase = await SavePurchaseAsync(
                purchase,
                playerId,
                purchase.PurchaseTokenHash,
                purchase.ProductId,
                purchase.ProductId,
                purchase.PurchaseType,
                PurchaseStatuses.GrantedFinalizationPending,
                acknowledge.ProviderPayloadJson,
                acknowledge.ErrorCode ?? BattlePassErrorCodes.PurchaseAcknowledgeFailed,
                acknowledge.ErrorMessage ?? "Purchase acknowledge failed.",
                entitlement.Id,
                purchase.FinalizationMode,
                finalizationAttemptCount,
                finalizationAttemptAtUtcMs,
                acknowledge.ErrorCode ?? BattlePassErrorCodes.PurchaseAcknowledgeFailed,
                acknowledge.ErrorMessage ?? "Purchase acknowledge failed.",
                null,
                ct);
            _logger.LogWarning(
                "{EventName} PurchaseId: {PurchaseId}, ErrorCode: {ErrorCode}, PurchaseTokenHash: {PurchaseTokenHash}",
                BattlePassTelemetry.PurchaseFailed,
                pendingPurchase.Id,
                pendingPurchase.ErrorCode,
                purchase.PurchaseTokenHash);
            return await BuildPendingFinalizationResponseAsync(
                pendingPurchase,
                playerId,
                seasonId,
                entitlement,
                ct);
        }

        await SavePurchaseAsync(
            purchase,
            playerId,
            purchase.PurchaseTokenHash,
            purchase.ProductId,
            purchase.ProductId,
            purchase.PurchaseType,
            PurchaseStatuses.Acknowledged,
            acknowledge.ProviderPayloadJson,
            null,
            null,
            entitlement.Id,
            purchase.FinalizationMode,
            finalizationAttemptCount,
            finalizationAttemptAtUtcMs,
            null,
            null,
            finalizationAttemptAtUtcMs,
            ct);
        return await BuildSuccessResponseAsync(
            playerId,
            seasonId,
            entitlement,
            VerifyGooglePurchaseStatuses.VerifiedAndAcknowledged,
            GooglePlayFinalizeStatuses.Acknowledged,
            isDuplicate: false,
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
        string? linkedEntitlementId,
        string finalizationMode,
        int finalizationAttemptCount,
        long? lastFinalizationAttemptAtUtcMs,
        string? finalizationErrorCode,
        string? finalizationErrorMessage,
        long? finalizedAtUtcMs,
        CancellationToken ct)
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
            FinalizationMode = string.IsNullOrWhiteSpace(existingPurchase?.FinalizationMode)
                ? finalizationMode
                : existingPurchase.FinalizationMode,
            FinalizationAttemptCount = finalizationAttemptCount,
            LastFinalizationAttemptAtUtcMs = lastFinalizationAttemptAtUtcMs,
            FinalizationErrorCode = finalizationErrorCode,
            FinalizationErrorMessage = finalizationErrorMessage,
            FinalizedAtUtcMs = finalizedAtUtcMs,
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
        string playerId,
        string seasonId,
        UserEntitlementRecord entitlement,
        string purchaseStatus,
        string googleFinalizeStatus,
        bool isDuplicate,
        CancellationToken ct)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = true,
            PurchaseStatus = isDuplicate ? VerifyGooglePurchaseStatuses.DuplicateAlreadyProcessed : purchaseStatus,
            GoogleFinalizeStatus = googleFinalizeStatus,
            Entitlement = BuildEntitlementDto(entitlement),
            BattlePass = await _battlePassService.GetSeasonStateAsync(playerId, seasonId, ct)
        };
    }

    private async Task<VerifyGooglePurchaseResponse> BuildPendingFinalizationResponseAsync(
        PurchaseRecord purchase,
        string playerId,
        string seasonId,
        UserEntitlementRecord entitlement,
        CancellationToken ct)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = false,
            PurchaseStatus = VerifyGooglePurchaseStatuses.GrantedButFinalizationPending,
            GoogleFinalizeStatus = GooglePlayFinalizeStatuses.FinalizationPendingManual,
            Entitlement = BuildEntitlementDto(entitlement),
            BattlePass = await _battlePassService.GetSeasonStateAsync(playerId, seasonId, ct),
            ErrorCode = purchase.ErrorCode ?? purchase.FinalizationErrorCode ?? BattlePassErrorCodes.PurchaseFinalizationPending,
            ErrorMessage = purchase.ErrorMessage ?? purchase.FinalizationErrorMessage ?? "Purchase finalization is pending manual retry."
        };
    }

    private static PurchaseEntitlementDto BuildEntitlementDto(UserEntitlementRecord entitlement)
    {
        return new PurchaseEntitlementDto
        {
            Type = entitlement.EntitlementType,
            Key = entitlement.EntitlementKey,
            Status = entitlement.Status
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

    private static string GetFinalizedStatus(string finalizationMode)
    {
        return string.Equals(finalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal)
            ? PurchaseStatuses.Consumed
            : PurchaseStatuses.Acknowledged;
    }

    private static string GetSuccessPurchaseStatus(string internalStatus)
    {
        return string.Equals(internalStatus, PurchaseStatuses.Consumed, StringComparison.Ordinal)
            ? VerifyGooglePurchaseStatuses.VerifiedAndConsumed
            : VerifyGooglePurchaseStatuses.VerifiedAndAcknowledged;
    }

    private static string GetGoogleFinalizeStatus(string internalStatus)
    {
        return string.Equals(internalStatus, PurchaseStatuses.Consumed, StringComparison.Ordinal)
            ? GooglePlayFinalizeStatuses.Consumed
            : GooglePlayFinalizeStatuses.Acknowledged;
    }

    private static bool IsProviderFinalized(GooglePlayPurchaseVerificationResult verification, string finalizationMode)
    {
        return string.Equals(finalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal)
            ? verification.IsConsumed
            : verification.IsAcknowledged;
    }

    private static bool IsPurchaseFinalized(PurchaseRecord purchase, string finalizationMode)
    {
        return string.Equals(finalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal)
            ? string.Equals(purchase.Status, PurchaseStatuses.Consumed, StringComparison.Ordinal)
            : string.Equals(purchase.Status, PurchaseStatuses.Acknowledged, StringComparison.Ordinal);
    }

    private static bool IsPendingFinalization(PurchaseRecord purchase, string finalizationMode)
    {
        if (string.Equals(purchase.Status, PurchaseStatuses.GrantedFinalizationPending, StringComparison.Ordinal) ||
            string.Equals(purchase.Status, PurchaseStatuses.Granted, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(finalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal) &&
               string.Equals(purchase.Status, PurchaseStatuses.Acknowledged, StringComparison.Ordinal);
    }

    private static bool IsSupportedFinalizationMode(string finalizationMode)
    {
        return string.Equals(finalizationMode, PurchaseFinalizationModes.Consume, StringComparison.Ordinal) ||
               string.Equals(finalizationMode, PurchaseFinalizationModes.Acknowledge, StringComparison.Ordinal);
    }

    private static VerifyGooglePurchaseResponse BuildVerifyFailure(
        string errorMessage,
        string errorCode = BattlePassErrorCodes.ValidationError)
    {
        return new VerifyGooglePurchaseResponse
        {
            Success = false,
            PurchaseStatus = VerifyGooglePurchaseStatuses.VerifyFailed,
            GoogleFinalizeStatus = GooglePlayFinalizeStatuses.NotAttempted,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
