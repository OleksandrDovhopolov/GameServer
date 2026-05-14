using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class IapServiceTests
{
    [Fact]
    public async Task VerifyGooglePurchaseAsync_SameTokenSamePlayerIsIdempotentForConsumedPurchase()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_1", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_1", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });

        var first = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);
        var second = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(first.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifiedAndConsumed, first.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.Consumed, first.GoogleFinalizeStatus);

        Assert.True(second.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.DuplicateAlreadyProcessed, second.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.Consumed, second.GoogleFinalizeStatus);
        Assert.Equal(BattlePassPassTypes.Premium, second.BattlePass!.PassType);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RejectsTokenReuseByAnotherPlayer()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1", "p2");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_1", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_1", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });

        await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);
        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p2",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseAlreadyUsed, response.ErrorCode);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifyFailed, response.PurchaseStatus);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_UpgradesPremiumToPlatinum()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.EntitlementService.GrantBattlePassEntitlementAsync(
            "p1",
            "season_1",
            BattlePassPassTypes.Premium,
            EntitlementSourceTypes.Admin,
            null,
            CancellationToken.None);
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_platinum_season_1",
            BattlePassPassTypes.Platinum,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_upgrade", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_platinum_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_upgrade", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_platinum_season_1",
            PurchaseToken = "token_upgrade",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifiedAndConsumed, response.PurchaseStatus);
        Assert.Equal(BattlePassPassTypes.Platinum, response.BattlePass!.PassType);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_ReturnsFailureForPendingPurchase()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_pending", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "pending",
            ProviderPayloadJson = "{}"
        });

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_pending",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifyFailed, response.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.NotAttempted, response.GoogleFinalizeStatus);
        Assert.Equal(BattlePassErrorCodes.PurchaseNotPurchased, response.ErrorCode);
        Assert.Equal(BattlePassPassTypes.None, await context.EntitlementService.GetBattlePassPassTypeAsync("p1", "season_1", now, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_ReturnsAcknowledgedDuplicateWithoutGrantingTwice()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Acknowledge), CancellationToken.None);

        var entitlement = (await context.EntitlementService.GrantBattlePassEntitlementAsync(
            "p1",
            "season_1",
            BattlePassPassTypes.Premium,
            EntitlementSourceTypes.Purchase,
            "pur_existing",
            CancellationToken.None)).Entitlement!;

        await context.PurchaseRepository.SaveAsync(new PurchaseRecord
        {
            Id = "pur_existing",
            PlayerId = "p1",
            Platform = PurchasePlatforms.GooglePlay,
            ProductId = "battle_pass_premium_season_1",
            PurchaseTokenHash = PurchaseTokenHasher.Hash("token_ack"),
            Status = PurchaseStatuses.Acknowledged,
            PurchaseType = PurchaseTypes.BattlePass,
            FinalizationMode = PurchaseFinalizationModes.Acknowledge,
            FinalizedAtUtcMs = now - 500,
            LinkedEntitlementId = entitlement.Id,
            ProviderPayloadJson = "{}",
            CreatedAtUtcMs = now - 1000,
            UpdatedAtUtcMs = now - 1000
        }, CancellationToken.None);
        await context.PurchaseRepository.TryCreateTokenIndexAsync(PurchaseTokenHasher.Hash("token_ack"), "pur_existing", CancellationToken.None);

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_ack",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.DuplicateAlreadyProcessed, response.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.Acknowledged, response.GoogleFinalizeStatus);
        Assert.Equal(BattlePassPassTypes.Premium, response.BattlePass!.PassType);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RejectsProviderProductMismatch()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_mismatch", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_platinum_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_mismatch",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseProductMismatch, response.ErrorCode);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_AllowsSharedProductIdsForFutureScheduledSeason()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.SeasonRepository.SaveAsync(new BattlePassSeasonRecord
        {
            Id = "season_2",
            Title = "Season 2",
            StartAtUtcMs = now + 20_000,
            EndAtUtcMs = now + 40_000,
            MaxLevel = 2,
            Status = BattlePassSeasonStatuses.Scheduled,
            ConfigVersion = "v1",
            PremiumProductId = "battle_pass_premium_season_1",
            PlatinumProductId = "battle_pass_platinum_season_1",
            CreatedAtUtcMs = now - 1000,
            UpdatedAtUtcMs = now - 1000
        }, CancellationToken.None);

        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_shared", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_shared", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_shared",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifiedAndConsumed, response.PurchaseStatus);
        Assert.Equal(BattlePassPassTypes.Premium, response.BattlePass!.PassType);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RejectsScheduledSeasonPurchase()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.SeasonRepository.SaveAsync(new BattlePassSeasonRecord
        {
            Id = "season_2",
            Title = "Season 2",
            StartAtUtcMs = now + 20_000,
            EndAtUtcMs = now + 40_000,
            MaxLevel = 2,
            Status = BattlePassSeasonStatuses.Scheduled,
            ConfigVersion = "v1",
            PremiumProductId = "battle_pass_premium_season_1",
            PlatinumProductId = "battle_pass_platinum_season_1",
            CreatedAtUtcMs = now - 1000,
            UpdatedAtUtcMs = now - 1000
        }, CancellationToken.None);

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_future",
            SeasonId = "season_2"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.SeasonNotPurchasable, response.ErrorCode);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RejectsProductOutsideRequestedSeason()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_other",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_other",
            PurchaseToken = "token_other",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.ProductNotFound, response.ErrorCode);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifyFailed, response.PurchaseStatus);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_ReturnsPendingFinalizationWhenConsumeFails()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_consume_fail", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_consume_fail", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = false,
            ErrorCode = BattlePassErrorCodes.PurchaseConsumeFailed,
            ErrorMessage = "consume failed",
            ProviderPayloadJson = "{}"
        });

        var response = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_consume_fail",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.GrantedButFinalizationPending, response.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.FinalizationPendingManual, response.GoogleFinalizeStatus);
        Assert.Equal(BattlePassErrorCodes.PurchaseConsumeFailed, response.ErrorCode);
        Assert.NotNull(response.Entitlement);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RetriesPendingFinalizationAndCompletesSuccessfully()
    {
        const long now = 1_750_000_000_000;
        var context = await CreateContextAsync(now, "p1");
        await context.ProductRepository.SaveAsync(CreateBattlePassProduct(
            "battle_pass_premium_season_1",
            BattlePassPassTypes.Premium,
            PurchaseFinalizationModes.Consume), CancellationToken.None);

        context.Verifier.SeedVerify("token_retry", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_premium_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        context.Verifier.SeedConsume("token_retry", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = false,
            ErrorCode = BattlePassErrorCodes.PurchaseConsumeFailed,
            ErrorMessage = "consume failed",
            ProviderPayloadJson = "{}"
        });

        var first = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_retry",
            SeasonId = "season_1"
        }, CancellationToken.None);

        context.Verifier.SeedConsume("token_retry", new GameServer.Api.Services.Interfaces.GooglePlayConsumeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });

        var second = await context.Service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_premium_season_1",
            PurchaseToken = "token_retry",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(first.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.GrantedButFinalizationPending, first.PurchaseStatus);

        Assert.True(second.Success);
        Assert.Equal(VerifyGooglePurchaseStatuses.VerifiedAndConsumed, second.PurchaseStatus);
        Assert.Equal(GooglePlayFinalizeStatuses.Consumed, second.GoogleFinalizeStatus);
    }

    private static async Task<IapServiceTestContext> CreateContextAsync(long now, params string[] playerIds)
    {
        var playerRepository = new InMemoryPlayerEconomyRepository();
        foreach (var playerId in playerIds)
        {
            playerRepository.Seed(playerId);
        }

        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason("season_1", now - 10_000, now + 10_000);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);

        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);

        var stateRepository = new InMemoryBattlePassStateRepository();
        var entitlementRepository = new InMemoryEntitlementRepository();
        var entitlementService = new EntitlementService(entitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var battlePassService = new BattlePassService(
            playerRepository,
            seasonRepository,
            stateRepository,
            entitlementService,
            rewardDefinitions,
            new RecordingRewardApplicationService(),
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);
        var productRepository = new InMemoryProductCatalogRepository();
        var purchaseRepository = new InMemoryPurchaseRepository();
        var verifier = new FakeGooglePlayPurchaseVerifier();
        var service = new IapService(
            playerRepository,
            productRepository,
            purchaseRepository,
            seasonRepository,
            entitlementService,
            verifier,
            new NoOpDistributedLockService(),
            battlePassService,
            new FixedClock(now),
            NullLogger<IapService>.Instance);

        return new IapServiceTestContext(
            service,
            verifier,
            seasonRepository,
            productRepository,
            purchaseRepository,
            entitlementService);
    }

    private static CatalogProductRecord CreateBattlePassProduct(
        string productId,
        string passType,
        string finalizationMode)
    {
        return new CatalogProductRecord
        {
            ProductId = productId,
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CatalogProductMetadataKeys.PassType] = passType,
                [CatalogProductMetadataKeys.FinalizationMode] = finalizationMode
            }
        };
    }

    private sealed record IapServiceTestContext(
        IapService Service,
        FakeGooglePlayPurchaseVerifier Verifier,
        InMemoryBattlePassSeasonRepository SeasonRepository,
        InMemoryProductCatalogRepository ProductRepository,
        InMemoryPurchaseRepository PurchaseRepository,
        EntitlementService EntitlementService);
}
