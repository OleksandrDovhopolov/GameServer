using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using Xunit;
using GameServer.Api.Models.Domain.BattlePass;

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
            new InMemoryPlayerAggregateRepository(new InMemorySaveRepository()),
            new InMemoryPurchaseRepository(),
            CreateBattlePassService(now, seasonRepository, rewardDefinitions: rewardDefinitions),
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
            new InMemoryPlayerAggregateRepository(new InMemorySaveRepository()),
            new InMemoryPurchaseRepository(),
            CreateBattlePassService(now, seasonRepository, rewardDefinitions: rewardDefinitions),
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

    [Fact]
    public async Task RecreateSeasonAsync_CreatesPublishesAndBootstrapsPlayers()
    {
        const long now = 1_750_000_000_000;
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var stateRepository = new InMemoryBattlePassStateRepository();
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        var entitlementRepository = new InMemoryEntitlementRepository();
        var productCatalogRepository = new InMemoryProductCatalogRepository();
        var saveRepository = new InMemorySaveRepository();
        var playerEconomyRepository = new SaveBackedPlayerEconomyRepository(saveRepository);
        var playerAggregateRepository = new InMemoryPlayerAggregateRepository(saveRepository);
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var battlePassService = CreateBattlePassService(
            now,
            seasonRepository,
            stateRepository,
            rewardDefinitions,
            playerEconomyRepository,
            entitlementRepository);
        var service = new BattlePassAdminService(
            seasonRepository,
            stateRepository,
            productCatalogRepository,
            rewardDefinitions,
            new EntitlementService(entitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance),
            playerEconomyRepository,
            playerAggregateRepository,
            new InMemoryPurchaseRepository(),
            battlePassService,
            new FixedClock(now));

        var response = await service.RecreateSeasonAsync(new RecreateBattlePassSeasonRequest
        {
            Season = new CreateBattlePassSeasonRequest
            {
                Id = "Test_Battle_Pass",
                Title = "Test Battle Pass",
                StartAtUtc = "2025-06-15T00:00:00Z",
                EndAtUtc = "2025-06-30T00:00:00Z",
                MaxLevel = 2,
                ConfigVersion = "v1",
                PremiumProductId = "battle_pass_premium_test",
                PlatinumProductId = "battle_pass_platinum_test"
            },
            Levels =
            [
                new BattlePassLevelAdminRequest
                {
                    Level = 0,
                    XpRequired = 0,
                    DefaultRewardId = "Gems",
                    PremiumRewardId = null
                },
                new BattlePassLevelAdminRequest
                {
                    Level = 1,
                    XpRequired = 100,
                    DefaultRewardId = "reward_default_1",
                    PremiumRewardId = "reward_premium_1"
                },
                new BattlePassLevelAdminRequest
                {
                    Level = 2,
                    XpRequired = 200,
                    DefaultRewardId = "reward_default_2",
                    PremiumRewardId = "reward_premium_2"
                }
            ],
            BootstrapPlayerIds = ["test-player-1"]
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(BattlePassSeasonStatuses.Active, response.Season!.Status);
        Assert.Equal(["test-player-1"], response.BootstrappedPlayerIds);

        var current = await battlePassService.GetCurrentAsync("test-player-1", CancellationToken.None);
        Assert.Equal("Test_Battle_Pass", current.Season!.Id);
        Assert.Equal(0, current.UserState!.Level);
        Assert.Equal(0, current.UserState.Xp);
        var claimable = Assert.Single(current.UserState.ClaimableRewards);
        Assert.Equal(0, claimable.Level);
        Assert.Equal(BattlePassRewardTracks.Default, claimable.RewardTrack);
        Assert.Equal("Gems", claimable.RewardId);

        var save = await saveRepository.LoadAsync("test-player-1", CancellationToken.None);
        Assert.NotNull(save);
        Assert.DoesNotContain("EventStates", save!.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPlayerProgressAsync_ResetsStateRevokesEntitlementAndRemovesEventState()
    {
        const long now = 1_750_000_000_000;
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var stateRepository = new InMemoryBattlePassStateRepository();
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        var entitlementRepository = new InMemoryEntitlementRepository();
        var saveRepository = new InMemorySaveRepository();
        var playerAggregateRepository = new InMemoryPlayerAggregateRepository(saveRepository);
        var playerEconomyRepository = new SaveBackedPlayerEconomyRepository(saveRepository);
        var productCatalogRepository = new InMemoryProductCatalogRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);

        var season = BattlePassTestFactory.CreateSeason(
            "Test_Battle_Pass",
            now - 20_000,
            now + 20_000,
            status: BattlePassSeasonStatuses.Active);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);

        var aggregate = await playerAggregateRepository.LoadAsync("p1", CancellationToken.None);
        aggregate.RootDocument["EventStates"] = new JsonArray
        {
            new JsonObject
            {
                ["ScheduleItemId"] = "Test_Battle_Pass",
                ["State"] = 3
            },
            new JsonObject
            {
                ["ScheduleItemId"] = "Another_Event",
                ["State"] = 2
            }
        };
        await playerAggregateRepository.SaveAsync(aggregate, CancellationToken.None);

        await stateRepository.SaveAsync(new UserBattlePassRecord
        {
            PlayerId = "p1",
            SeasonId = season.Id,
            Xp = 200,
            Level = 2,
            ClaimedRewards =
            [
                new ClaimedBattlePassRewardRecord
                {
                    Key = "1:default",
                    Level = 1,
                    RewardTrack = BattlePassRewardTracks.Default,
                    ClaimedAtUtcMs = now - 1000
                }
            ],
            CreatedAtUtcMs = now - 5000,
            UpdatedAtUtcMs = now - 4000
        }, CancellationToken.None);

        var entitlementService = new EntitlementService(entitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var grant = await entitlementService.GrantBattlePassEntitlementAsync(
            "p1",
            season.Id,
            BattlePassPassTypes.Premium,
            EntitlementSourceTypes.Admin,
            null,
            CancellationToken.None);
        Assert.True(grant.Success);

        var battlePassService = CreateBattlePassService(
            now,
            seasonRepository,
            stateRepository: stateRepository,
            rewardDefinitions: rewardDefinitions,
            playerRepository: playerEconomyRepository,
            entitlementRepository: entitlementRepository);
        var service = new BattlePassAdminService(
            seasonRepository,
            stateRepository,
            productCatalogRepository,
            rewardDefinitions,
            entitlementService,
            playerEconomyRepository,
            playerAggregateRepository,
            new InMemoryPurchaseRepository(),
            battlePassService,
            new FixedClock(now));

        var response = await service.ResetPlayerProgressAsync(new ResetBattlePassPlayerRequest
        {
            PlayerId = "p1",
            SeasonId = season.Id
        }, CancellationToken.None);

        Assert.True(response.Success);

        var state = await stateRepository.GetAsync("p1", season.Id, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(0, state!.Xp);
        Assert.Equal(0, state.Level);
        Assert.Empty(state.ClaimedRewards);

        var passType = await entitlementService.GetBattlePassPassTypeAsync("p1", season.Id, now, CancellationToken.None);
        Assert.Equal(BattlePassPassTypes.None, passType);

        var saved = await saveRepository.LoadAsync("p1", CancellationToken.None);
        Assert.NotNull(saved);
        var root = JsonNode.Parse(saved!.Data)!.AsObject();
        var states = root["EventStates"]!.AsArray();
        Assert.Single(states);
        var remaining = states[0]!.AsObject();
        Assert.Equal("Another_Event", remaining["ScheduleItemId"]!.GetValue<string>());
    }

    private static BattlePassService CreateBattlePassService(
        long now,
        InMemoryBattlePassSeasonRepository seasonRepository,
        InMemoryBattlePassStateRepository? stateRepository = null,
        InMemoryRewardDefinitionRepository? rewardDefinitions = null,
        IPlayerEconomyRepository? playerRepository = null,
        InMemoryEntitlementRepository? entitlementRepository = null)
    {
        var effectivePlayerRepository = playerRepository ?? new InMemoryPlayerEconomyRepository();
        var effectiveEntitlementRepository = entitlementRepository ?? new InMemoryEntitlementRepository();
        var effectiveRewardDefinitions = rewardDefinitions ?? new InMemoryRewardDefinitionRepository();
        var effectiveStateRepository = stateRepository ?? new InMemoryBattlePassStateRepository();
        var entitlementService = new EntitlementService(effectiveEntitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance);
        return new BattlePassService(
            effectivePlayerRepository,
            seasonRepository,
            effectiveStateRepository,
            entitlementService,
            effectiveRewardDefinitions,
            new RecordingRewardApplicationService(),
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);
    }

    private sealed class SaveBackedPlayerEconomyRepository : IPlayerEconomyRepository
    {
        private readonly InMemorySaveRepository _saveRepository;

        public SaveBackedPlayerEconomyRepository(InMemorySaveRepository saveRepository)
        {
            _saveRepository = saveRepository;
        }

        public Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct)
        {
            return _saveRepository.ExistsAsync(playerId, ct);
        }
    }
}
