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

    [Fact]
    public async Task AddXp_WhenSuccess_ReturnsOk()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            AddXpToCurrentSeasonResponse = new BattlePassStateDto
            {
                SeasonId = "season_1",
                Xp = 20,
                PassType = "none"
            }
        });

        var result = await controller.AddXp(new AddBattlePassXpRequest
        {
            PlayerId = "p1",
            Amount = 20
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AddBattlePassXpResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(20, response.AddedXp);
        Assert.Equal("season_1", response.BattlePass!.SeasonId);
    }

    [Fact]
    public async Task AddXp_WhenValidationFails_ReturnsBadRequest()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            AddXpException = new ArgumentException("amount must be positive.")
        });

        var result = await controller.AddXp(new AddBattlePassXpRequest
        {
            PlayerId = "p1",
            Amount = 0
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<AddBattlePassXpResponse>(badRequest.Value);
        Assert.False(response.Success);
        Assert.Equal(GameServer.Api.Services.BattlePassErrorCodes.ValidationError, response.ErrorCode);
    }

    [Fact]
    public async Task AddXp_WhenPlayerMissing_ReturnsNotFound()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            AddXpException = new KeyNotFoundException("missing")
        });

        var result = await controller.AddXp(new AddBattlePassXpRequest
        {
            PlayerId = "p1",
            Amount = 20
        }, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        var response = Assert.IsType<AddBattlePassXpResponse>(notFound.Value);
        Assert.False(response.Success);
        Assert.Equal(GameServer.Api.Services.BattlePassErrorCodes.PlayerNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task AddXp_WhenNoActiveSeason_ReturnsConflict()
    {
        var controller = new BattlePassController(new StubBattlePassService
        {
            AddXpException = new InvalidOperationException("No active battle pass season is available.")
        });

        var result = await controller.AddXp(new AddBattlePassXpRequest
        {
            PlayerId = "p1",
            Amount = 20
        }, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, conflict.StatusCode);
        var response = Assert.IsType<AddBattlePassXpResponse>(conflict.Value);
        Assert.False(response.Success);
        Assert.Equal(GameServer.Api.Services.BattlePassErrorCodes.NoActiveSeason, response.ErrorCode);
    }

    private sealed class StubBattlePassService : IBattlePassService
    {
        public Exception? CurrentException { get; init; }
        public BattlePassClaimResponse ClaimResponse { get; init; } = new() { Success = true };
        public BattlePassStateDto AddXpToCurrentSeasonResponse { get; init; } = new() { SeasonId = "season_1" };
        public Exception? AddXpException { get; init; }

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

        public Task<BattlePassStateDto> AddXpToCurrentSeasonAsync(string playerId, int amount, CancellationToken ct = default)
        {
            if (AddXpException is not null)
            {
                throw AddXpException;
            }

            return Task.FromResult(AddXpToCurrentSeasonResponse);
        }

        public Task<BattlePassStateDto> AddXpAsync(string playerId, string seasonId, int amount, string source, CancellationToken ct = default)
            => Task.FromResult(new BattlePassStateDto { SeasonId = seasonId });
    }
}
