using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;

namespace GameServer.Api.Services;

//TODO move it from here
public sealed class TestRepository : IPlayerAggregateRepository
{
    private readonly ISaveRepository _saveRepository;

    public TestRepository(ISaveRepository saveRepository)
    {
        _saveRepository = saveRepository;
    }

    public async Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var save = await _saveRepository.LoadAsync(playerId, ct);
        JsonObject root = [];
        long lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (save is not null)
        {
            lastModified = save.LastModified;
            if (TryParseRootObject(save.Data, out var parsed))
            {
                root = parsed;
            }
        }

        return new PlayerAggregate
        {
            PlayerId = playerId,
            RootDocument = root,
            LastModified = lastModified
        };
    }

    public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _saveRepository.SaveAsync(player.PlayerId, player.RootDocument.ToJsonString(), ct);
    }

    public Task DeleteAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _saveRepository.DeleteAsync(playerId, ct);
    }

    private static bool TryParseRootObject(string payload, out JsonObject root)
    {
        try
        {
            var parsed = JsonNode.Parse(payload);
            if (parsed is JsonObject parsedRoot)
            {
                root = parsedRoot;
                return true;
            }
        }
        catch
        {
            // ignored
        }

        root = default!;
        return false;
    }
}
