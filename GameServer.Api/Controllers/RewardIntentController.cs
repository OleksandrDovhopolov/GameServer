using GameServer.Api.Models.Dto.RewardIntents;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/rewards/intent")]
public sealed class RewardIntentController : ControllerBase
{
    private readonly ILogger<RewardIntentController> _logger;
    private readonly IRewardIntentService _rewardIntentService;

    public RewardIntentController(
        ILogger<RewardIntentController> logger,
        IRewardIntentService rewardIntentService)
    {
        _logger = logger;
        _rewardIntentService = rewardIntentService;
    }

    [HttpPost("create")]
    public async Task<ActionResult<CreateRewardIntentResponse>> Create([FromBody] CreateRewardIntentRequest request, CancellationToken ct)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["PlayerId"] = request.PlayerId,
                   ["RewardId"] = request.RewardId
               }))
        {
            var response = await _rewardIntentService.CreateIntentAsync(request.PlayerId, request.RewardId, ct);
            return Ok(response);
        }
    }

    [HttpGet("status")]
    public async Task<ActionResult<GetRewardIntentStatusResponse>> GetStatus([FromQuery] string? rewardIntentId, CancellationToken ct)
    {
        var response = await _rewardIntentService.GetIntentStatusAsync(rewardIntentId ?? string.Empty, ct);
        return response.ErrorCode switch
        {
            RewardIntentErrorCodes.ValidationError => BadRequest(response),
            RewardIntentErrorCodes.IntentNotFound => NotFound(response),
            _ => Ok(response)
        };
    }

    [HttpGet("callback")]
    public async Task<ActionResult<ConfirmRewardIntentCallbackGetResponse>> ConfirmCallbackGet(
        [FromQuery(Name = "rewardIntentId")] string? playerId,
        [FromQuery(Name = "eventId")] string? eventId,
        [FromQuery] string? rewards,
        [FromQuery] string? dynamicUserId,
        [FromQuery] string? auctionId,
        [FromQuery] string? adNetwork,
        [FromQuery] string? appKey,
        [FromQuery] string? itemName,
        [FromQuery] string? placementName,
        CancellationToken ct)
    {
        var provider = string.IsNullOrWhiteSpace(adNetwork) ? "unknown" : adNetwork!;
        var payload = JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["playerId"] = playerId,
            ["dynamicUserId"] = dynamicUserId,
            ["rewards"] = rewards,
            ["auctionId"] = auctionId,
            ["adNetwork"] = adNetwork,
            ["appKey"] = appKey,
            ["itemName"] = itemName,
            ["placementName"] = placementName
        });

        var response = await _rewardIntentService.ConfirmIntentFromAdCallbackGetAsync(
            provider,
            dynamicUserId ?? string.Empty,
            eventId ?? string.Empty,
            payload,
            ct);

        return response.ErrorCode switch
        {
            RewardIntentErrorCodes.ValidationError => BadRequest(response),
            RewardIntentErrorCodes.IntentNotFound => NotFound(response),
            _ => Ok(response)
        };
    }
}
