using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.RewardIntents;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardIntentControllerTests
{
    [Fact]
    public async Task Create_WhenDomainValidationFails_ReturnsOkWithFailurePayload()
    {
        var controller = CreateController(new StubService
        {
            CreateResponse = new CreateRewardIntentResponse
            {
                Success = false,
                ErrorCode = RewardIntentErrorCodes.ValidationError,
                ErrorMessage = "validation"
            }
        });

        var result = await controller.Create(new CreateRewardIntentRequest { PlayerId = "", RewardId = "" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CreateRewardIntentResponse>(ok.Value);
        Assert.False(payload.Success);
        Assert.Equal(RewardIntentErrorCodes.ValidationError, payload.ErrorCode);
    }

    [Fact]
    public async Task GetStatus_WhenValidationError_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService
        {
            StatusResponse = new GetRewardIntentStatusResponse
            {
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.ValidationError
            }
        });

        var result = await controller.GetStatus(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetStatus_WhenNotFound_ReturnsNotFound()
    {
        var controller = CreateController(new StubService
        {
            StatusResponse = new GetRewardIntentStatusResponse
            {
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.IntentNotFound
            }
        });

        var result = await controller.GetStatus("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetStatus_WhenFulfilled_ReturnsOk()
    {
        var controller = CreateController(new StubService
        {
            StatusResponse = new GetRewardIntentStatusResponse
            {
                Status = RewardIntentStatuses.Fulfilled
            }
        });

        var result = await controller.GetStatus("ri_1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<GetRewardIntentStatusResponse>(ok.Value);
        Assert.Equal("fulfilled", payload.Status);
    }

    [Fact]
    public async Task Callback_WhenValidationError_ReturnsBadRequest()
    {
        var controller = CreateController(new StubService
        {
            CallbackResponse = new ConfirmRewardIntentCallbackResponse
            {
                Success = false,
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.ValidationError
            }
        });

        var result = await controller.ConfirmCallback(new ConfirmRewardIntentCallbackRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static RewardIntentController CreateController(IRewardIntentService service)
    {
        return new RewardIntentController(
            NullLogger<RewardIntentController>.Instance,
            service);
    }

    private sealed class StubService : IRewardIntentService
    {
        public CreateRewardIntentResponse CreateResponse { get; init; } = new() { Success = true, RewardIntentId = "ri_1" };
        public GetRewardIntentStatusResponse StatusResponse { get; init; } = new() { Status = RewardIntentStatuses.Pending };
        public ConfirmRewardIntentCallbackResponse CallbackResponse { get; init; } = new() { Success = true, Status = RewardIntentStatuses.Fulfilled };

        public Task<CreateRewardIntentResponse> CreateIntentAsync(string playerId, string rewardId, CancellationToken ct = default)
        {
            return Task.FromResult(CreateResponse);
        }

        public Task<GetRewardIntentStatusResponse> GetIntentStatusAsync(string rewardIntentId, CancellationToken ct = default)
        {
            return Task.FromResult(StatusResponse);
        }

        public Task<ConfirmRewardIntentCallbackResponse> ConfirmIntentFromAdCallbackAsync(
            string provider,
            string rewardIntentId,
            string externalEventId,
            string? callbackPayload,
            CancellationToken ct = default)
        {
            return Task.FromResult(CallbackResponse);
        }
    }
}
