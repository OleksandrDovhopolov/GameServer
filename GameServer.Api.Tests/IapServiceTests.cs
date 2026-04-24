using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.Iap;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class IapServiceTests
{
    [Fact]
    public async Task VerifyGooglePurchaseAsync_SameTokenSamePlayerIsIdempotent()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
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
        await productRepository.SaveAsync(new CatalogProductRecord
        {
            ProductId = "battle_pass_gold_season_1",
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = "season_1",
                ["passType"] = BattlePassPassTypes.Gold
            }
        }, CancellationToken.None);
        var verifier = new FakeGooglePlayPurchaseVerifier();
        verifier.SeedVerify("token_1", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_gold_season_1",
            PurchaseState = "purchased",
            IsAcknowledged = false,
            ProviderPayloadJson = "{}"
        });
        verifier.SeedAcknowledge("token_1", new GameServer.Api.Services.Interfaces.GooglePlayAcknowledgeResult
        {
            Success = true,
            ProviderPayloadJson = "{}"
        });
        var service = new IapService(
            playerRepository,
            productRepository,
            new InMemoryPurchaseRepository(),
            seasonRepository,
            entitlementService,
            verifier,
            new NoOpDistributedLockService(),
            battlePassService,
            new FixedClock(now),
            NullLogger<IapService>.Instance);

        var first = await service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_gold_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);
        var second = await service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_gold_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(PurchaseStatuses.Acknowledged, second.PurchaseStatus);
        Assert.Equal(BattlePassPassTypes.Gold, second.BattlePass!.PassType);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_RejectsTokenReuseByAnotherPlayer()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
        playerRepository.Seed("p2");
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
        await productRepository.SaveAsync(new CatalogProductRecord
        {
            ProductId = "battle_pass_gold_season_1",
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = "season_1",
                ["passType"] = BattlePassPassTypes.Gold
            }
        }, CancellationToken.None);
        var verifier = new FakeGooglePlayPurchaseVerifier();
        verifier.SeedVerify("token_1", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_gold_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        var purchaseRepository = new InMemoryPurchaseRepository();
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

        await service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_gold_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);
        var response = await service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p2",
            ProductId = "battle_pass_gold_season_1",
            PurchaseToken = "token_1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.PurchaseAlreadyUsed, response.ErrorCode);
    }

    [Fact]
    public async Task VerifyGooglePurchaseAsync_UpgradesGoldToPlatinum()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason("season_1", now - 10_000, now + 10_000);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var stateRepository = new InMemoryBattlePassStateRepository();
        var entitlementRepository = new InMemoryEntitlementRepository();
        var entitlementService = new EntitlementService(entitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance);
        await entitlementService.GrantBattlePassEntitlementAsync("p1", "season_1", BattlePassPassTypes.Gold, EntitlementSourceTypes.Admin, null, CancellationToken.None);
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
        await productRepository.SaveAsync(new CatalogProductRecord
        {
            ProductId = "battle_pass_platinum_season_1",
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = "season_1",
                ["passType"] = BattlePassPassTypes.Platinum
            }
        }, CancellationToken.None);
        var verifier = new FakeGooglePlayPurchaseVerifier();
        verifier.SeedVerify("token_upgrade", new GameServer.Api.Services.Interfaces.GooglePlayPurchaseVerificationResult
        {
            Success = true,
            ProductId = "battle_pass_platinum_season_1",
            PurchaseState = "purchased",
            ProviderPayloadJson = "{}"
        });
        var service = new IapService(
            playerRepository,
            productRepository,
            new InMemoryPurchaseRepository(),
            seasonRepository,
            entitlementService,
            verifier,
            new NoOpDistributedLockService(),
            battlePassService,
            new FixedClock(now),
            NullLogger<IapService>.Instance);

        var response = await service.VerifyGooglePurchaseAsync(new VerifyGooglePurchaseRequest
        {
            PlayerId = "p1",
            ProductId = "battle_pass_platinum_season_1",
            PurchaseToken = "token_upgrade",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(BattlePassPassTypes.Platinum, response.BattlePass!.PassType);
    }
}
