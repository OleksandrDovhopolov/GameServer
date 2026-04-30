using System.Net;
using GameServer.Api.Configuration;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Google;
using Google.Apis.Requests;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class GooglePlayPurchaseVerifierTests
{
    [Fact]
    public async Task VerifyAsync_MapsPurchasedResponse()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            GetResult = new GooglePlayProductPurchasePayload
            {
                ProductId = "battle_pass_premium_season_1",
                PurchaseState = 0,
                AcknowledgementState = 1,
                ProviderPayloadJson = "{\"purchaseState\":0}"
            }
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("battle_pass_premium_season_1", result.ProductId);
        Assert.Equal("purchased", result.PurchaseState);
        Assert.True(result.IsAcknowledged);
        Assert.Equal("{\"purchaseState\":0}", result.ProviderPayloadJson);
    }

    [Fact]
    public async Task VerifyAsync_MapsPendingResponse()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            GetResult = new GooglePlayProductPurchasePayload
            {
                ProductId = "battle_pass_premium_season_1",
                PurchaseState = 2,
                AcknowledgementState = 0,
                ProviderPayloadJson = "{\"purchaseState\":2}"
            }
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("pending", result.PurchaseState);
        Assert.False(result.IsAcknowledged);
    }

    [Fact]
    public async Task VerifyAsync_MapsNotFoundToPurchaseNotPurchased()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            GetException = new GoogleApiException("androidpublisher", "not found")
            {
                HttpStatusCode = HttpStatusCode.NotFound,
                Error = new RequestError { Message = "not found" }
            }
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseNotPurchased, result.ErrorCode);
        Assert.Contains("404", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyAsync_MapsForbiddenToPurchaseVerifyFailed()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            GetException = new GoogleApiException("androidpublisher", "forbidden")
            {
                HttpStatusCode = HttpStatusCode.Forbidden,
                Error = new RequestError { Message = "forbidden" }
            }
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseVerifyFailed, result.ErrorCode);
        Assert.Contains("403", result.ErrorMessage);
    }

    [Fact]
    public async Task AcknowledgeAsync_ReturnsProviderPayload()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            AcknowledgeResult = "{\"acknowledged\":true}"
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.AcknowledgeAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("{\"acknowledged\":true}", result.ProviderPayloadJson);
    }

    [Fact]
    public async Task AcknowledgeAsync_MapsApiFailure()
    {
        var client = new FakeGooglePlayProductPurchaseClient
        {
            AcknowledgeException = new GoogleApiException("androidpublisher", "bad request")
            {
                HttpStatusCode = HttpStatusCode.BadRequest,
                Error = new RequestError { Message = "bad request" }
            }
        };
        var verifier = CreateVerifier(client);

        var result = await verifier.AcknowledgeAsync("battle_pass_premium_season_1", "token_1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseAcknowledgeFailed, result.ErrorCode);
        Assert.Contains("400", result.ErrorMessage);
    }

    private static GooglePlayPurchaseVerifier CreateVerifier(IGooglePlayProductPurchaseClient client)
    {
        return new GooglePlayPurchaseVerifier(
            client,
            Options.Create(new GooglePlayBillingOptions
            {
                Enabled = true,
                PackageName = "com.example.game",
                ServiceAccountKeyFile = "unused-in-unit-tests.json"
            }));
    }

    private sealed class FakeGooglePlayProductPurchaseClient : IGooglePlayProductPurchaseClient
    {
        public GooglePlayProductPurchasePayload GetResult { get; set; } = new();
        public Exception? GetException { get; set; }
        public string AcknowledgeResult { get; set; } = "{}";
        public Exception? AcknowledgeException { get; set; }

        public Task<GooglePlayProductPurchasePayload> GetAsync(string packageName, string productId, string purchaseToken, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (GetException is not null)
            {
                throw GetException;
            }

            return Task.FromResult(GetResult);
        }

        public Task<string> AcknowledgeAsync(string packageName, string productId, string purchaseToken, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (AcknowledgeException is not null)
            {
                throw AcknowledgeException;
            }

            return Task.FromResult(AcknowledgeResult);
        }
    }
}
