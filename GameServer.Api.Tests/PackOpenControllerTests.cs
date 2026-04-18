using GameServer.Api.Models.Dto;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class PackOpenControllerTests
{
    [Fact]
    public async Task OpenPack_WhenServiceThrowsArgumentException_ReturnsBadRequest()
    {
        var controller = new global::PackOpenController(
            NullLogger<global::PackOpenController>.Instance,
            new ValidationErrorPackService());
        var request = new OpenPackRequest
        {
            PlayerId = "p1",
            PackId = "starter_pack",
            EventId = string.Empty,
            OpenPackRequestId = "open-1"
        };

        var actionResult = await controller.OpenPack(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        var message = Assert.IsType<string>(badRequest.Value);
        Assert.Contains("EventId is required.", message);
    }

    private sealed class ValidationErrorPackService : IPackService
    {
        public Task<OpenPackResponse> OpenPackAsync(OpenPackRequest request, CancellationToken ct)
        {
            throw new ArgumentException("EventId is required.", "EventId");
        }
    }
}
