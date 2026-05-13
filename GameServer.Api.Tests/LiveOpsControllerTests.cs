using System.Text.Json;
using GameServer.Api.Controllers;
using GameServer.Api.Models.Dto.LiveOps;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class LiveOpsControllerTests
{
    [Fact]
    public async Task GetSchedule_WhenSuccess_ReturnsOkWithExpectedPayload()
    {
        var controller = new LiveOpsController(
            NullLogger<LiveOpsController>.Instance,
            new StubLiveOpsScheduleService
            {
                Response = new PublicLiveOpsScheduleResponseDto
                {
                    ServerTimeUtc = "2026-04-20T22:25:43.4664352+00:00",
                    Items =
                    [
                        new PublicLiveOpsScheduleItemDto
                        {
                            Id = "Winter_Collection",
                            EventType = "CardCollection",
                            StartTimeUtc = "2026-04-16T08:39:03.4664352+00:00",
                            EndTimeUtc = "2026-04-27T22:25:43.4664352+00:00",
                            Priority = 10,
                            StreamId = "card_collection_seasons",
                            CustomParams = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["eventId"] = "Winter_Collection"
                            }
                        }
                    ]
                }
            });

        var result = await controller.GetSchedule(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<PublicLiveOpsScheduleResponseDto>(ok.Value);
        Assert.Equal("2026-04-20T22:25:43.4664352+00:00", payload.ServerTimeUtc);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"items\":[", json);
        Assert.Contains("\"Id\":\"Winter_Collection\"", json);
        Assert.Contains("\"Priority\":10", json);
        Assert.Contains("\"CustomParams\":{\"eventId\":\"Winter_Collection\"}", json);
    }

    [Fact]
    public async Task GetSchedule_WhenServiceFails_ReturnsInternalServerError()
    {
        var controller = new LiveOpsController(
            NullLogger<LiveOpsController>.Instance,
            new StubLiveOpsScheduleService
            {
                Exception = new FileNotFoundException("missing")
            });

        var result = await controller.GetSchedule(CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal("Internal error while loading liveops schedule.", error.Value);
    }

    private sealed class StubLiveOpsScheduleService : ILiveOpsScheduleService
    {
        public PublicLiveOpsScheduleResponseDto Response { get; init; } = new();
        public Exception? Exception { get; init; }

        public Task<PublicLiveOpsScheduleResponseDto> GetScheduleAsync(CancellationToken ct = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Response);
        }
    }
}
