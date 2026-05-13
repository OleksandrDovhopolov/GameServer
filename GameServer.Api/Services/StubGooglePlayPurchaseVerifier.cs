using System.Text.Json;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class StubGooglePlayPurchaseVerifier : IGooglePlayPurchaseVerifier
{
    private readonly ILogger<StubGooglePlayPurchaseVerifier> _logger;

    public StubGooglePlayPurchaseVerifier(ILogger<StubGooglePlayPurchaseVerifier> logger)
    {
        _logger = logger;
    }

    public Task<GooglePlayPurchaseVerificationResult> VerifyAsync(string productId, string purchaseToken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogWarning(
            "Stub Google Play verifier handled verify request. ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            productId,
            string.IsNullOrWhiteSpace(purchaseToken) ? string.Empty : PurchaseTokenHasher.Hash(purchaseToken));
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(purchaseToken))
        {
            return Task.FromResult(new GooglePlayPurchaseVerificationResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.ValidationError,
                ErrorMessage = "productId and purchaseToken are required."
            });
        }

        if (purchaseToken.StartsWith("verifyfail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new GooglePlayPurchaseVerificationResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.PurchaseVerifyFailed,
                ErrorMessage = "Stub verifier failed the purchase token.",
                ProviderPayloadJson = BuildPayload(productId, purchaseToken, "verification_failed", false, false)
            });
        }

        var purchaseState = "purchased";
        if (purchaseToken.StartsWith("pending", StringComparison.OrdinalIgnoreCase))
        {
            purchaseState = "pending";
        }
        else if (purchaseToken.StartsWith("refunded", StringComparison.OrdinalIgnoreCase))
        {
            purchaseState = "refunded";
        }
        else if (purchaseToken.StartsWith("revoked", StringComparison.OrdinalIgnoreCase))
        {
            purchaseState = "revoked";
        }

        var acknowledged = purchaseToken.Contains("acknowledged", StringComparison.OrdinalIgnoreCase);
        var consumed = purchaseToken.Contains("consumed", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = productId,
            PurchaseState = purchaseState,
            IsAcknowledged = acknowledged,
            IsConsumed = consumed,
            ProviderPayloadJson = BuildPayload(productId, purchaseToken, purchaseState, acknowledged, consumed)
        });
    }

    public Task<GooglePlayAcknowledgeResult> AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogWarning(
            "Stub Google Play verifier handled acknowledge request. ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            productId,
            string.IsNullOrWhiteSpace(purchaseToken) ? string.Empty : PurchaseTokenHasher.Hash(purchaseToken));
        if (purchaseToken.StartsWith("ackfail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new GooglePlayAcknowledgeResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.PurchaseAcknowledgeFailed,
                ErrorMessage = "Stub acknowledge failed.",
                ProviderPayloadJson = BuildPayload(productId, purchaseToken, "ack_failed", false, false)
            });
        }

        return Task.FromResult(new GooglePlayAcknowledgeResult
        {
            Success = true,
            ProviderPayloadJson = BuildPayload(productId, purchaseToken, "acknowledged", true, false)
        });
    }

    public Task<GooglePlayConsumeResult> ConsumeAsync(string productId, string purchaseToken, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.LogWarning(
            "Stub Google Play verifier handled consume request. ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            productId,
            string.IsNullOrWhiteSpace(purchaseToken) ? string.Empty : PurchaseTokenHasher.Hash(purchaseToken));
        if (purchaseToken.StartsWith("consumefail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new GooglePlayConsumeResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.PurchaseConsumeFailed,
                ErrorMessage = "Stub consume failed.",
                ProviderPayloadJson = BuildPayload(productId, purchaseToken, "consume_failed", false, false)
            });
        }

        return Task.FromResult(new GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = BuildPayload(productId, purchaseToken, "consumed", false, true)
        });
    }

    private static string BuildPayload(string productId, string purchaseToken, string state, bool acknowledged, bool consumed)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["productId"] = productId,
            ["purchaseTokenHash"] = PurchaseTokenHasher.Hash(purchaseToken),
            ["state"] = state,
            ["acknowledged"] = acknowledged,
            ["consumed"] = consumed
        });
    }
}
