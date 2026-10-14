using System.Text.Json;
using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface IGameEventPublisher
{
    Task<GameEventPublishResult> PublishAsync(
        string playerId,
        string eventName,
        JsonElement? payload,
        CancellationToken cancellationToken);
}
