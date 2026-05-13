using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class BattlePassServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_LazilyCreatesStateAndReturnsNonePass()
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
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var stateRepository = new InMemoryBattlePassStateRepository();
        var service = new BattlePassService(
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

        var response = await service.GetCurrentAsync("p1", CancellationToken.None);

        Assert.NotNull(response.Season);
        Assert.NotNull(response.Products);
        Assert.NotNull(response.UserState);
        Assert.Equal("battle_pass_premium_season_1", response.Products!.PremiumProductId);
        Assert.Equal("battle_pass_platinum_season_1", response.Products.PlatinumProductId);
        Assert.Equal(BattlePassPassTypes.None, response.UserState!.PassType);
        Assert.Equal(0, response.UserState.Level);
        Assert.Equal("Gems", response.Levels[0].DefaultRewardId);
        Assert.Null(response.Levels[0].PremiumRewardId);
        Assert.Equal("Gems", response.UserState.ClaimableRewards[0].RewardId);
        Assert.DoesNotContain(response.UserState.ClaimableRewards, x => x.Level == 0 && x.RewardTrack == BattlePassRewardTracks.Premium);
        var storedState = await stateRepository.GetAsync("p1", "season_1", CancellationToken.None);
        Assert.NotNull(storedState);
        Assert.Equal(0, storedState!.Level);
    }

    [Fact]
    public async Task ClaimAsync_RejectsPremiumTrackWithoutEntitlement()
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
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
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

        var response = await service.ClaimAsync(new GameServer.Api.Models.Dto.BattlePass.ClaimBattlePassRewardRequest
        {
            PlayerId = "p1",
            SeasonId = "season_1",
            Level = 0,
            RewardTrack = BattlePassRewardTracks.Premium
        }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(BattlePassErrorCodes.RewardTrackNotAllowed, response.ErrorCode);
    }

    [Fact]
    public async Task ClaimAsync_AllowsDefaultTrackAtLevelZero()
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
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var rewardService = new RecordingRewardApplicationService();
        var service = new BattlePassService(
            playerRepository,
            seasonRepository,
            new InMemoryBattlePassStateRepository(),
            entitlementService,
            rewardDefinitions,
            rewardService,
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);

        var response = await service.ClaimAsync(new GameServer.Api.Models.Dto.BattlePass.ClaimBattlePassRewardRequest
        {
            PlayerId = "p1",
            SeasonId = "season_1",
            Level = 0,
            RewardTrack = BattlePassRewardTracks.Default
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.GrantedRewards);
        Assert.Equal("Gems", response.GrantedRewards[0].RewardId);
        Assert.Contains("Gems", rewardService.GrantedRewardIds);
    }

    [Fact]
    public async Task AddXpAsync_CapsAtMaxLevel()
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
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
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

        var state = await service.AddXpAsync("p1", "season_1", 10_000, "debug", CancellationToken.None);

        Assert.Equal(2, state.Level);
        Assert.Equal(10_000, state.Xp);
    }

    [Fact]
    public async Task AddXpToCurrentSeasonAsync_AddsXpToActiveSeason()
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
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
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

        var state = await service.AddXpToCurrentSeasonAsync("p1", 20, CancellationToken.None);

        Assert.Equal("season_1", state.SeasonId);
        Assert.Equal(20, state.Xp);
    }

    [Fact]
    public async Task AddXpToCurrentSeasonAsync_WhenNoActiveSeason_ThrowsInvalidOperationException()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason(
            "season_1",
            now + 10_000,
            now + 20_000,
            status: BattlePassSeasonStatuses.Scheduled);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddXpToCurrentSeasonAsync("p1", 20, CancellationToken.None));

        Assert.Equal("No active battle pass season is available.", exception.Message);
    }

    [Fact]
    public async Task AddXpToCurrentSeasonAsync_WhenPlayerMissing_ThrowsKeyNotFoundException()
    {
        const long now = 1_750_000_000_000;
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var season = BattlePassTestFactory.CreateSeason("season_1", now - 10_000, now + 10_000);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
            new InMemoryPlayerEconomyRepository(),
            seasonRepository,
            new InMemoryBattlePassStateRepository(),
            entitlementService,
            rewardDefinitions,
            new RecordingRewardApplicationService(),
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AddXpToCurrentSeasonAsync("missing", 20, CancellationToken.None));
    }

    [Fact]
    public async Task AddXpToCurrentSeasonAsync_WhenAmountInvalid_ThrowsArgumentException()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var entitlementService = new EntitlementService(new InMemoryEntitlementRepository(), new FixedClock(now), NullLogger<EntitlementService>.Instance);
        var service = new BattlePassService(
            playerRepository,
            new InMemoryBattlePassSeasonRepository(),
            new InMemoryBattlePassStateRepository(),
            entitlementService,
            rewardDefinitions,
            new RecordingRewardApplicationService(),
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddXpToCurrentSeasonAsync("p1", 0, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimAllAsync_UsesRewardServiceAndMarksClaims()
    {
        const long now = 1_750_000_000_000;
        var playerRepository = new InMemoryPlayerEconomyRepository();
        playerRepository.Seed("p1");
        var seasonRepository = new InMemoryBattlePassSeasonRepository();
        var stateRepository = new InMemoryBattlePassStateRepository();
        var season = BattlePassTestFactory.CreateSeason("season_1", now - 10_000, now + 10_000);
        await seasonRepository.SaveAsync(season, CancellationToken.None);
        await seasonRepository.SaveLevelsAsync(season.Id, BattlePassTestFactory.CreateLevels(season.Id), CancellationToken.None);
        await stateRepository.SaveAsync(new UserBattlePassRecord
        {
            PlayerId = "p1",
            SeasonId = "season_1",
            Xp = 200,
            Level = 2,
            CreatedAtUtcMs = now,
            UpdatedAtUtcMs = now
        }, CancellationToken.None);
        var rewardDefinitions = new InMemoryRewardDefinitionRepository();
        BattlePassTestFactory.SeedRewardDefinitions(rewardDefinitions);
        var entitlementRepository = new InMemoryEntitlementRepository();
        var entitlementService = new EntitlementService(entitlementRepository, new FixedClock(now), NullLogger<EntitlementService>.Instance);
        await entitlementService.GrantBattlePassEntitlementAsync("p1", "season_1", BattlePassPassTypes.Premium, EntitlementSourceTypes.Admin, null, CancellationToken.None);
        var rewardService = new RecordingRewardApplicationService();
        var service = new BattlePassService(
            playerRepository,
            seasonRepository,
            stateRepository,
            entitlementService,
            rewardDefinitions,
            rewardService,
            new NoOpDistributedLockService(),
            new FixedClock(now),
            new StaticLiveOpsScheduleItemProvider(),
            NullLogger<BattlePassService>.Instance);

        var response = await service.ClaimAllAsync(new GameServer.Api.Models.Dto.BattlePass.ClaimAllBattlePassRewardsRequest
        {
            PlayerId = "p1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(5, rewardService.GrantedRewardIds.Count);
        Assert.Equal(5, response.BattlePass!.ClaimedRewards.Count);
        Assert.Equal(5, response.GrantedRewards.Count);
        Assert.Contains(response.GrantedRewards, x => x.Level == 0 && x.RewardTrack == BattlePassRewardTracks.Default && x.RewardId == "Gems");
        Assert.Contains(response.GrantedRewards, x => x.Level == 1 && x.RewardTrack == BattlePassRewardTracks.Default && x.RewardId == "reward_default_1");
        Assert.Contains(response.GrantedRewards, x => x.Level == 1 && x.RewardTrack == BattlePassRewardTracks.Premium && x.RewardId == "reward_premium_1");
        Assert.DoesNotContain(response.GrantedRewards, x => x.Level == 0 && x.RewardTrack == BattlePassRewardTracks.Premium);
    }
}
