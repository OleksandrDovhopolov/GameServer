using System.Text.Json;
using GameServer.Api.Models.Domain;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

/// <summary>
/// Used when Kafka:Enabled is false, so the events endpoint stays available without a broker.
/// </summary>
public sealed class NullGameEventPublisher : IGameEventPublisher
{
    private readonly ILogger<NullGameEventPublisher> _logger;

    public NullGameEventPublisher(ILogger<NullGameEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task<GameEventPublishResult> PublishAsync(
        string playerId,
        string eventName,
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Game event dropped, Kafka publishing is disabled. Event: {Event}", eventName);
        return Task.FromResult(new GameEventPublishResult(false, -1, -1));
    }
}
