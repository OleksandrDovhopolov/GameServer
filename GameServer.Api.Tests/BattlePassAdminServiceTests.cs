using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class BattlePassAdminServiceTests
{
    [Fact]
    public async Task PublishSeasonAsync_RejectsLevelsThatDoNotStartFromZero()
    {
        const long now = 1_750_000_000_000;
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason(
            "season_1",
            now - 10_000,
            now + 10_000,
            status: GameServer.Api.Models.Domain.BattlePass.BattlePassSeasonStatuses.Draft);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id,
        [
            new()
            {
                SeasonId = season.Id,
                Level = 1,
                XpRequired = 0,
                DefaultRewardId = "reward_default_1",
                PremiumRewardId = "reward_premium_1"
            },
            new()
            {
                SeasonId = season.Id,
                Level = 2,
                XpRequired = 100,
                DefaultRewardId = "reward_default_2",
                PremiumRewardId = "reward_premium_2"
            }
        ], CancellationToken.None);

        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var service = new BattlePassAdminService(
            seasonRepository,
            new InMemoryBattlePassStateRepository(),
            new InMemoryProductCatalogRepository(),
            rewardDefinitions,
            new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance),
            new InMemoryPlayerEconomyRepository(),
            new InMemoryPurchaseRepository(),
            CreateBattlePassService(now, seasonRepository, rewardDefinitions),
            new FixedClock(now));

        var response = await service.PublishSeasonAsync(season.Id, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.SeasonValidationFailed, response.ErrorCode);
        Assert.Equal("Configured levels count must match maxLevel + 1.", response.ErrorMessage);
    }

    [Fact]
    public async Task UpsertLevelsAsync_StoresNullPremiumRewardId()
    {
        const long now = 1_750_000_000_000;
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason(
            "season_1",
            now - 10_000,
            now + 10_000,
            status: GameServer.Api.Models.Domain.BattlePass.BattlePassSeasonStatuses.Draft);
        await seasonRepository.SaveAsync(season, CancellationToken.None);

        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var service = new BattlePassAdminService(
            seasonRepository,
            new InMemoryBattlePassStateRepository(),
            new InMemoryProductCatalogRepository(),
            rewardDefinitions,
            new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance),
            new InMemoryPlayerEconomyRepository(),
            new InMemoryPurchaseRepository(),
            CreateBattlePassService(now, seasonRepository, rewardDefinitions),
            new FixedClock(now));

        var response = await service.UpsertLevelsAsync(season.Id, new UpsertBattlePassLevelsRequest
        {
            Levels =
            [
                new()
                {
                    Level = 0,
                    XpRequired = 0,
                    DefaultRewardId = "Gems",
                    PremiumRewardId = null
                },
                new()
                {
                    Level = 1,
                    XpRequired = 100,
                    DefaultRewardId = "reward_default_1",
                    PremiumRewardId = "reward_premium_1"
                },
                new()
                {
                    Level = 2,
                    XpRequired = 200,
                    DefaultRewardId = "reward_default_2",
                    PremiumRewardId = "reward_premium_2"
                }
            ]
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Null(response.Levels[0].PremiumRewardId);
    }

    private static BattlePassService CreateBattlePassService(
        long now,
        InMemoryBattlePassSeasonRepository seasonRepository,
        InMemoryRewardDefinitionRepository rewardDefinitions)
    {
        var playerRepository = new InMemoryPlayerEconomyRepository();
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        return new BattlePassService(
            playerRepository,
            seasonRepository,
            new InMemoryBattlePassStateRepository(),
            entitlementService,
            rewardDefinitions,
            new RecordingRewardApplicationService(),
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);
    }
}
