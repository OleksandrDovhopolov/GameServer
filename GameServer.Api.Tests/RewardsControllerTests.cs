using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardsControllerTests
{
    [Fact]
    public async Task Grant_WhenSuccess_ReturnsOk()
    {
        var controller = CreateController(new StubRewardService(new GrantRewardResponse
        {
            Success = true,
            RewardId = "reward-1"
        }));

        var result = await controller.Grant(CreateCommand(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Grant_WhenInvalidRequest_ReturnsBadRequest()
    {
        var controller = CreateController(new StubRewardService(new GrantRewardResponse
        {
            Success = false,
            RewardId = "reward-1",
            ErrorCode = RewardErrorCodes.InvalidRequest
        }));

        var result = await controller.Grant(CreateCommand(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Grant_WhenPlayerNotFound_ReturnsNotFound()
    {
        var controller = CreateController(new StubRewardService(new GrantRewardResponse
        {
            Success = false,
            RewardId = "reward-1",
            ErrorCode = RewardErrorCodes.PlayerNotFound
        }));

        var result = await controller.Grant(CreateCommand(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Grant_WhenUnsupportedRewardKind_ReturnsInternalServerError()
    {
        var controller = CreateController(new StubRewardService(new GrantRewardResponse
        {
            Success = false,
            RewardId = "reward-1",
            ErrorCode = RewardErrorCodes.UnsupportedRewardKind
        }));

        var result = await controller.Grant(CreateCommand(), CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    private static RewardsController CreateController(IRewardApplicationService rewardApplicationService)
    {
        return new RewardsController(
            NullLogger<RewardsController>.Instance,
            rewardApplicationService);
    }

    private static GrantRewardCommand CreateCommand()
    {
        return new GrantRewardCommand
        {
            PlayerId = "p1",
            RewardSource = "ad",
            RewardId = "reward-1"
        };
    }

    private sealed class StubRewardService : IRewardApplicationService
    {
        private readonly GrantRewardResponse _response;

        public StubRewardService(GrantRewardResponse response)
        {
            _response = response;
        }

        public Task<GrantRewardResponse> GrantAsync(GrantRewardCommand command, CancellationToken ct = default)
        {
            return Task.FromResult(_response);
        }
    }
}
