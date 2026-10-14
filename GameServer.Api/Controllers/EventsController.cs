using Confluent.Kafka;
using GameServer.Api.Models.Dto.Events;
using GameServer.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController : ControllerBase
{
    private readonly ILogger<EventsController> _logger;
    private readonly IGameEventPublisher _publisher;

    public EventsController(ILogger<EventsController> logger, IGameEventPublisher publisher)
    {
        _logger = logger;
        _publisher = publisher;
    }

    [HttpPost]
    public async Task<ActionResult<GameEventResponse>> Publish(
        [FromBody] GameEventRequest request,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["PlayerId"] = request.PlayerId }))
        {
            try
            {
                var result = await _publisher.PublishAsync(
                    request.PlayerId,
                    request.Event,
                    request.Payload,
                    cancellationToken);

                return Accepted(new GameEventResponse(result.Published, result.Partition, result.Offset));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Event request validation failed.");
                return BadRequest(ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Event request canceled.");
                throw;
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Kafka rejected the event. Reason: {Reason}", ex.Error.Reason);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Event broker is unavailable.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event request failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal error while publishing the event.");
            }
        }
    }
}
