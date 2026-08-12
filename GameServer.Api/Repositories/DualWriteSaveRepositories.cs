using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories.Interfaces;

namespace GameServer.Api.Repositories;

public sealed class DualWriteSaveRepository : ISaveRepository
{
    private readonly ISaveRepository _primary;
    private readonly ISaveRepository _secondary;
    private readonly ILogger<DualWriteSaveRepository> _logger;

    public DualWriteSaveRepository(
        ISaveRepository primary,
        ISaveRepository secondary,
        ILogger<DualWriteSaveRepository> logger)
    {
        _primary = primary;
        _secondary = secondary;
        _logger = logger;
    }

    public Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken) =>
        _primary.LoadAsync(playerId, cancellationToken);

    public Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken) =>
        _primary.GetLastModifiedTimestampAsync(playerId, cancellationToken);

    public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken) =>
        _primary.ExistsAsync(playerId, cancellationToken);

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        await _primary.SaveAsync(playerId, data, cancellationToken);
        await MirrorAsync(
            () => _secondary.SaveAsync(playerId, data, cancellationToken),
            "save",
            playerId);
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        await _primary.DeleteAsync(playerId, cancellationToken);
        await MirrorAsync(
            () => _secondary.DeleteAsync(playerId, cancellationToken),
            "delete",
            playerId);
    }

    private async Task MirrorAsync(Func<Task> action, string operation, string playerId)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Secondary save repository mirror failed. Operation: {Operation}, PlayerId: {PlayerId}", operation, playerId);
        }
    }
}

public sealed class DualWritePlayerAggregateRepository : IPlayerAggregateRepository
{
    private readonly IPlayerAggregateRepository _primary;
    private readonly IPlayerAggregateRepository _secondary;
    private readonly ILogger<DualWritePlayerAggregateRepository> _logger;

    public DualWritePlayerAggregateRepository(
        IPlayerAggregateRepository primary,
        IPlayerAggregateRepository secondary,
        ILogger<DualWritePlayerAggregateRepository> logger)
    {
        _primary = primary;
        _secondary = secondary;
        _logger = logger;
    }

    public Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct) =>
        _primary.LoadAsync(playerId, ct);

    public async Task SaveAsync(PlayerAggregate player, CancellationToken ct)
    {
        await _primary.SaveAsync(player, ct);
        var primaryLastModified = player.LastModified;
        await MirrorAsync(
            () => _secondary.SaveAsync(Clone(player), ct),
            "save",
            player.PlayerId);
        player.LastModified = primaryLastModified;
    }

    public async Task DeleteAsync(string playerId, CancellationToken ct)
    {
        await _primary.DeleteAsync(playerId, ct);
        await MirrorAsync(
            () => _secondary.DeleteAsync(playerId, ct),
            "delete",
            playerId);
    }

    private async Task MirrorAsync(Func<Task> action, string operation, string playerId)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Secondary aggregate repository mirror failed. Operation: {Operation}, PlayerId: {PlayerId}", operation, playerId);
        }
    }

    private static PlayerAggregate Clone(PlayerAggregate source) =>
        new()
        {
            PlayerId = source.PlayerId,
            RootDocument = source.RootDocument.DeepClone().AsObject(),
            LastModified = source.LastModified,
            Resources = new Dictionary<string, int>(source.Resources, StringComparer.Ordinal),
            InventoryItems = new Dictionary<string, int>(source.InventoryItems, StringComparer.Ordinal)
        };
}

public sealed class DualWritePlayerResourcesRepository : IPlayerResourcesRepository
{
    private readonly IPlayerResourcesRepository _primary;
    private readonly IPlayerResourcesRepository _secondary;
    private readonly ILogger<DualWritePlayerResourcesRepository> _logger;

    public DualWritePlayerResourcesRepository(
        IPlayerResourcesRepository primary,
        IPlayerResourcesRepository secondary,
        ILogger<DualWritePlayerResourcesRepository> logger)
    {
        _primary = primary;
        _secondary = secondary;
        _logger = logger;
    }

    public Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct) =>
        _primary.GetAsync(playerId, ct);

    public async Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct)
    {
        await _primary.SaveAsync(record, ct);
        await MirrorAsync(
            () => _secondary.SaveAsync(Clone(record), ct),
            "save",
            record.PlayerId);
    }

    private async Task MirrorAsync(Func<Task> action, string operation, string playerId)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Secondary resources repository mirror failed. Operation: {Operation}, PlayerId: {PlayerId}", operation, playerId);
        }
    }

    private static PlayerResourcesRecord Clone(PlayerResourcesRecord source) =>
        new()
        {
            PlayerId = source.PlayerId,
            Resources = new Dictionary<string, int>(source.Resources, StringComparer.Ordinal),
            Version = source.Version
        };
}

public sealed class DualWritePlayerInventoryRepository : IPlayerInventoryRepository
{
    private readonly IPlayerInventoryRepository _primary;
    private readonly IPlayerInventoryRepository _secondary;
    private readonly ILogger<DualWritePlayerInventoryRepository> _logger;

    public DualWritePlayerInventoryRepository(
        IPlayerInventoryRepository primary,
        IPlayerInventoryRepository secondary,
        ILogger<DualWritePlayerInventoryRepository> logger)
    {
        _primary = primary;
        _secondary = secondary;
        _logger = logger;
    }

    public Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct) =>
        _primary.GetAsync(playerId, ct);

    public async Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct)
    {
        await _primary.SaveAsync(record, ct);
        await MirrorAsync(
            () => _secondary.SaveAsync(Clone(record), ct),
            "save",
            record.PlayerId);
    }

    private async Task MirrorAsync(Func<Task> action, string operation, string playerId)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Secondary inventory repository mirror failed. Operation: {Operation}, PlayerId: {PlayerId}", operation, playerId);
        }
    }

    private static PlayerInventoryRecord Clone(PlayerInventoryRecord source) =>
        new()
        {
            PlayerId = source.PlayerId,
            Items = new Dictionary<string, int>(source.Items, StringComparer.Ordinal),
            Version = source.Version
        };
}
