using System.Text.Json;
using System.Text;
using GameServer.Api.Configuration;
using GameServer.Api.Services.Interfaces;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace GameServer.Api.Services;

public sealed class GooglePlayPublisherApiClient : IGooglePlayProductPurchaseClient
{
    private static readonly string[] Scopes =
    [
        AndroidPublisherService.Scope.Androidpublisher
    ];

    private readonly AndroidPublisherService _service;
    private readonly ILogger<GooglePlayPublisherApiClient> _logger;

    public GooglePlayPublisherApiClient(
        IOptions<GooglePlayBillingOptions> options,
        ILogger<GooglePlayPublisherApiClient> logger)
    {
        var value = options.Value;
        _logger = logger;
        var credential = CreateCredential(value).CreateScoped(Scopes);
        var initializer = new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = value.ApplicationName,
            HttpClientFactory = new TimeoutHttpClientFactory(TimeSpan.FromSeconds(Math.Max(1, value.HttpTimeoutSeconds)))
        };

        _service = new AndroidPublisherService(initializer);
        _logger.LogInformation(
            "Google Play publisher API client initialized. PackageNameSet: {PackageNameSet}, ServiceAccountSource: {ServiceAccountSource}, ApplicationName: {ApplicationName}, HttpTimeoutSeconds: {HttpTimeoutSeconds}",
            !string.IsNullOrWhiteSpace(value.PackageName),
            string.IsNullOrWhiteSpace(value.ServiceAccountJson) ? "file" : "json",
            value.ApplicationName,
            value.HttpTimeoutSeconds);
    }

    private static GoogleCredential CreateCredential(GooglePlayBillingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(options.ServiceAccountJson));
            return GoogleCredential.FromStream(stream);
        }

        return GoogleCredential.FromFile(options.ServiceAccountKeyFile);
    }

    public async Task<GooglePlayProductPurchasePayload> GetAsync(
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Calling Google Play Purchases.Products.Get. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            packageName,
            productId,
            PurchaseTokenHasher.Hash(purchaseToken));
        var response = await _service.Purchases.Products.Get(packageName, productId, purchaseToken).ExecuteAsync(ct);
        return new GooglePlayProductPurchasePayload
        {
            ProductId = response.ProductId ?? string.Empty,
            PurchaseState = response.PurchaseState ?? 0,
            AcknowledgementState = response.AcknowledgementState ?? 0,
            ConsumptionState = response.ConsumptionState ?? 0,
            ProviderPayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = response.Kind,
                ["purchaseTimeMillis"] = response.PurchaseTimeMillis,
                ["purchaseState"] = response.PurchaseState,
                ["consumptionState"] = response.ConsumptionState,
                ["developerPayload"] = response.DeveloperPayload,
                ["orderId"] = response.OrderId,
                ["purchaseType"] = response.PurchaseType,
                ["acknowledgementState"] = response.AcknowledgementState,
                ["productId"] = response.ProductId,
                ["quantity"] = response.Quantity,
                ["refundableQuantity"] = response.RefundableQuantity,
                ["regionCode"] = response.RegionCode,
                ["obfuscatedExternalAccountId"] = response.ObfuscatedExternalAccountId,
                ["obfuscatedExternalProfileId"] = response.ObfuscatedExternalProfileId
            })
        };
    }

    public async Task<string> AcknowledgeAsync(
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Calling Google Play Purchases.Products.Acknowledge. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            packageName,
            productId,
            PurchaseTokenHasher.Hash(purchaseToken));
        var request = _service.Purchases.Products.Acknowledge(
            new ProductPurchasesAcknowledgeRequest(),
            packageName,
            productId,
            purchaseToken);
        await request.ExecuteAsync(ct);
        return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["packageName"] = packageName,
            ["productId"] = productId,
            ["acknowledged"] = true
        });
    }

    public async Task<string> ConsumeAsync(
        string packageName,
        string productId,
        string purchaseToken,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Calling Google Play Purchases.Products.Consume. PackageName: {PackageName}, ProductId: {ProductId}, PurchaseTokenHash: {PurchaseTokenHash}",
            packageName,
            productId,
            PurchaseTokenHasher.Hash(purchaseToken));
        var request = _service.Purchases.Products.Consume(
            packageName,
            productId,
            purchaseToken);
        await request.ExecuteAsync(ct);
        return JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["packageName"] = packageName,
            ["productId"] = productId,
            ["consumed"] = true
        });
    }

    private sealed class TimeoutHttpClientFactory(TimeSpan timeout) : Google.Apis.Http.IHttpClientFactory
    {
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
        {
            var client = new Google.Apis.Http.HttpClientFactory().CreateHttpClient(args);
            client.Timeout = timeout;
            return client;
        }
    }
}
