using System.Net;
using System.Text.Json;
using GameServer.Api.Configuration;
using GameServer.Api.Services.Interfaces;
using Google;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Services;

public sealed class GooglePlayPurchaseVerifier : IGooglePlayPurchaseVerifier
{
    private readonly IGooglePlayProductPurchaseClient _client;
    private readonly GooglePlayBillingOptions _options;
    private readonly ILogger<GooglePlayPurchaseVerifier> _logger;

    public GooglePlayPurchaseVerifier(
        IGooglePlayProductPurchaseClient client,
        IOptions<GooglePlayBillingOptions> options,
        ILogger<GooglePlayPurchaseVerifier> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GooglePlayPurchaseVerificationResult> VerifyAsync(
        string productId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(purchaseToken))
        {
            return new GooglePlayPurchaseVerificationResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.ValidationError,
                ErrorMessage = "productId and purchaseToken are required."
            };
        }

        try
        {
            _logger.LogInformation(
                "Google Play verify request started. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken));
            var purchase = await _client.GetAsync(_options.PackageName, productId, purchaseToken, ct);
            _logger.LogInformation(
                "Google Play verify request completed. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}, PurchaseState: {PurchaseState}, AcknowledgementState: {AcknowledgementState}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken),
                purchase.PurchaseState,
                purchase.AcknowledgementState);
            return new GooglePlayPurchaseVerificationResult
            {
                Success = true,
                ProductId = purchase.ProductId,
                PurchaseState = MapPurchaseState(purchase.PurchaseState),
                IsAcknowledged = purchase.AcknowledgementState != 0,
                ProviderPayloadJson = purchase.ProviderPayloadJson
            };
        }
        catch (GoogleApiException ex)
        {
            _logger.LogWarning(
                ex,
                "Google Play verify request failed. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}, HttpStatusCode: {HttpStatusCode}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken),
                ex.HttpStatusCode);
            return new GooglePlayPurchaseVerificationResult
            {
                Success = false,
                ErrorCode = ex.HttpStatusCode == HttpStatusCode.NotFound
                    ? BattlePassErrorCodes.PurchaseNotPurchased
                    : BattlePassErrorCodes.PurchaseVerifyFailed,
                ErrorMessage = BuildGoogleApiErrorMessage("verify", ex),
                ProviderPayloadJson = BuildGoogleApiErrorPayload("verify", productId, ex)
            };
        }
    }

    public async Task<GooglePlayAcknowledgeResult> AcknowledgeAsync(
        string productId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(purchaseToken))
        {
            return new GooglePlayAcknowledgeResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.ValidationError,
                ErrorMessage = "productId and purchaseToken are required."
            };
        }

        try
        {
            _logger.LogInformation(
                "Google Play acknowledge request started. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken));
            var payload = await _client.AcknowledgeAsync(_options.PackageName, productId, purchaseToken, ct);
            _logger.LogInformation(
                "Google Play acknowledge request completed. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken));
            return new GooglePlayAcknowledgeResult
            {
                Success = true,
                ProviderPayloadJson = payload
            };
        }
        catch (GoogleApiException ex)
        {
            _logger.LogWarning(
                ex,
                "Google Play acknowledge request failed. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}, HttpStatusCode: {HttpStatusCode}",
                _options.PackageName,
                productId,
                PurchaseTokenHasher.Hash(purchaseToken),
                ex.HttpStatusCode);
            return new GooglePlayAcknowledgeResult
            {
                Success = false,
                ErrorCode = BattlePassErrorCodes.PurchaseAcknowledgeFailed,
                ErrorMessage = BuildGoogleApiErrorMessage("acknowledge", ex),
                ProviderPayloadJson = BuildGoogleApiErrorPayload("acknowledge", productId, ex)
            };
        }
    }

    private static string MapPurchaseState(int purchaseState)
    {
        return purchaseState switch
        {
            0 => "purchased",
            2 => "pending",
            _ => "revoked"
        };
    }

    private static string BuildGoogleApiErrorMessage(string operation, GoogleApiException ex)
    {
        var statusCode = GetHttpStatusCode(ex);
        var status = statusCode.HasValue ? $"{statusCode.Value} {ex.HttpStatusCode}" : "unknown";
        var detail = ex.Error?.Message ?? ex.Message;
        return $"Google Play {operation} failed with {status}: {detail}";
    }

    private static string BuildGoogleApiErrorPayload(string operation, string productId, GoogleApiException ex)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["productId"] = productId,
            ["httpStatusCode"] = GetHttpStatusCode(ex),
            ["errorMessage"] = ex.Error?.Message ?? ex.Message,
            ["errorErrors"] = ex.Error?.Errors?.Select(x => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["reason"] = x.Reason,
                ["message"] = x.Message,
                ["location"] = x.Location,
                ["domain"] = x.Domain
            }).ToArray()
        });
    }

    private static int? GetHttpStatusCode(GoogleApiException ex)
    {
        var code = (int)ex.HttpStatusCode;
        return code == 0 ? null : code;
    }
}
