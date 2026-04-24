using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class BattlePassControllerTests
{
    [Fact]
    public async Task GetCurrent_WhenPlayerMissing_ReturnsNotFound()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            CurrentException = new KeyNotFoundException("missing")
        });

        var result = await controller.GetCurrent("p1", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Claim_WhenSeasonMissing_ReturnsNotFound()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            ClaimResponse = new BattlePassClaimResponse
            {
                Success = false,
                ErrorCode = GameServer.Api.Services.BattlePassErrorCodes.SeasonNotFound
            }
        });

        var result = await controller.Claim(new ClaimBattlePassRewardRequest
        {
            PlayerId = "p1",
            SeasonId = "missing",
            Level = 1,
            RewardTrack = "default"
        }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ClaimAll_WhenSuccess_ReturnsOk()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            ClaimResponse = new BattlePassClaimResponse
            {
                Success = true,
                BattlePass = new BattlePassStateDto { SeasonId = "season_1", PassType = "gold" }
            }
        });

        var result = await controller.ClaimAll(new ClaimAllBattlePassRewardsRequest
        {
            PlayerId = "p1",
            SeasonId = "season_1"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    private sealed class StubBattlePassService : IBattlePassService
    {
        public Exception? CurrentException { get; init; }
        public BattlePassClaimResponse ClaimResponse { get; init; } = new() { Success = true };

        public Task<BattlePassCurrentResponse> GetCurrentAsync(string playerId, CancellationToken ct = default)
        {
            if (CurrentException is not null)
            {
                throw CurrentException;
            }

            return Task.FromResult(new BattlePassCurrentResponse());
        }

        public Task<BattlePassScheduleResponse> GetScheduleAsync(CancellationToken ct = default) => Task.FromResult(new BattlePassScheduleResponse());

        public Task<BattlePassClaimResponse> ClaimAsync(ClaimBattlePassRewardRequest request, CancellationToken ct = default) => Task.FromResult(ClaimResponse);

        public Task<BattlePassClaimResponse> ClaimAllAsync(ClaimAllBattlePassRewardsRequest request, CancellationToken ct = default) => Task.FromResult(ClaimResponse);

        public Task<BattlePassStateDto> GetSeasonStateAsync(string playerId, string seasonId, CancellationToken ct = default)
            => Task.FromResult(new BattlePassStateDto { SeasonId = seasonId });

        public Task<BattlePassStateDto> AddXpAsync(string playerId, string seasonId, int amount, string source, CancellationToken ct = default)
            => Task.FromResult(new BattlePassStateDto { SeasonId = seasonId });
    }
}
