using System.Text;
using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class SaveService : ISaveService
{
    private const int MaxSaveBytes = 30 * 1024;
    private const string FortuneWheelPropertyName = "FortuneWheel";
    private const string AvailableSpinsPropertyName = "AvailableSpins";
    private const string UpdatedAtPropertyName = "UpdatedAt";
    private const string ResourcesPropertyName = "Resources";
    private const string InventoryPropertyName = "Inventory";
    private const string InventoryItemsPropertyName = "InventoryItems";
    private const string ServerRewardsPropertyName = "ServerRewards";

    private readonly ISaveRepository _saveRepository;
    private readonly IPlayerAggregateRepository _playerAggregateRepository;
    private readonly IGameSaveAssembler _gameSaveAssembler;
    private readonly ISystemClock _clock;
    private readonly ILogger<SaveService> _logger;
    
    public SaveService(
        ISaveRepository saveRepository,
        IPlayerAggregateRepository playerAggregateRepository,
        IGameSaveAssembler gameSaveAssembler,
        ISystemClock clock,
        ILogger<SaveService> logger)
    {
        _saveRepository = saveRepository;
        _playerAggregateRepository = playerAggregateRepository;
        _gameSaveAssembler = gameSaveAssembler;
        _clock = clock;
        _logger = logger;
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        ValidateData(data);

        var guardedData = await ApplyWheelSaveGuardsAsync(playerId, data, cancellationToken);
        if (!TryParseRootObject(guardedData, out var root))
        {
            throw new ArgumentException("Data must be a JSON object.", nameof(data));
        }

        SanitizeBlobRoot(root);
        var aggregate = await _playerAggregateRepository.LoadAsync(playerId, cancellationToken);
        ReplaceRootDocument(aggregate.RootDocument, root);
        await _playerAggregateRepository.SaveAsync(aggregate, cancellationToken);
    }

    public async Task<SaveRecord> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var aggregate = await _playerAggregateRepository.LoadAsync(playerId, cancellationToken);
        return new SaveRecord
        {
            Data = _gameSaveAssembler.BuildSaveJson(aggregate),
            LastModified = aggregate.LastModified
        };
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var exists = await _saveRepository.ExistsAsync(playerId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
        }

        await _playerAggregateRepository.DeleteAsync(playerId, cancellationToken);
    }

    public async Task<long> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var timestamp = await _saveRepository.GetLastModifiedTimestampAsync(playerId, cancellationToken);
        return timestamp ?? throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
    }

    private static void ValidatePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("PlayerId is required.", nameof(playerId));
        }
    }

    private static void ValidateData(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException("Data is required.", nameof(data));
        }

        var dataBytes = Encoding.UTF8.GetByteCount(data);
        if (dataBytes > MaxSaveBytes)
        {
            throw new ArgumentException($"Data exceeds the maximum allowed size of {MaxSaveBytes} bytes.", nameof(data));
        }
    }

    private async Task<string> ApplyWheelSaveGuardsAsync(string playerId, string incomingData, CancellationToken cancellationToken)
    {
        if (!TryParseRootObject(incomingData, out var incomingRoot))
        {
            _logger.LogWarning("Save guard skipped for {PlayerId} because incoming payload is not a JSON object.", playerId);
            return incomingData;
        }

        JsonObject? existingRoot = null;
        var existingRecord = await _saveRepository.LoadAsync(playerId, cancellationToken);
        if (existingRecord is not null && !string.IsNullOrWhiteSpace(existingRecord.Data))
        {
            if (TryParseRootObject(existingRecord.Data, out var parsedExistingRoot))
            {
                existingRoot = parsedExistingRoot;
            }
            else
            {
                _logger.LogWarning("Save guard could not parse existing payload for {PlayerId}; proceeding with incoming payload only.", playerId);
            }
        }

        var actions = new List<string>();
        var incomingFortuneWheel = GetOrCreateIncomingFortuneWheel(incomingRoot, existingRoot, actions);
        var existingFortuneWheel = GetFortuneWheelObject(existingRoot);

        if (incomingFortuneWheel is not null)
        {
            CanonicalizeWheelState(incomingFortuneWheel, existingFortuneWheel, actions);
        }

        _logger.LogInformation(
            "Save guard actions applied for {PlayerId}. ActionCount: {ActionCount}, Actions: {Actions}",
            playerId,
            actions.Count,
            actions.Count == 0 ? "None" : string.Join(", ", actions));

        return incomingRoot.ToJsonString();
    }

    private JsonObject? GetOrCreateIncomingFortuneWheel(JsonObject incomingRoot, JsonObject? existingRoot, List<string> actions)
    {
        if (incomingRoot.TryGetPropertyValue(FortuneWheelPropertyName, out var incomingWheelNode))
        {
            if (incomingWheelNode is JsonObject incomingWheelObject)
            {
                return incomingWheelObject;
            }

            if (GetFortuneWheelObject(existingRoot) is JsonObject existingWheelFromNonObject)
            {
                var clone = existingWheelFromNonObject.DeepClone().AsObject();
                incomingRoot[FortuneWheelPropertyName] = clone;
                actions.Add("ReplacedNonObjectFortuneWheelWithExisting");
                return clone;
            }

            var created = new JsonObject();
            incomingRoot[FortuneWheelPropertyName] = created;
            actions.Add("ReplacedNonObjectFortuneWheelWithEmptyObject");
            return created;
        }

        if (GetFortuneWheelObject(existingRoot) is JsonObject existingWheel)
        {
            var clone = existingWheel.DeepClone().AsObject();
            incomingRoot[FortuneWheelPropertyName] = clone;
            actions.Add("PreservedMissingFortuneWheelFromExisting");
            return clone;
        }

        return null;
    }

    private void CanonicalizeWheelState(JsonObject incomingFortuneWheel, JsonObject? existingFortuneWheel, List<string> actions)
    {
        var now = _clock.UtcNowUnixMilliseconds;

        var incomingState = ReadWheelState(incomingFortuneWheel, now);
        var existingState = ReadWheelState(existingFortuneWheel, now);

        var availableSpins = incomingState.AvailableSpins;
        if (availableSpins is null && existingState.AvailableSpins is not null)
        {
            availableSpins = existingState.AvailableSpins;
            actions.Add("MergedMissingAvailableSpinsFromExisting");
        }

        var updatedAt = incomingState.UpdatedAt;
        if (updatedAt is null && existingState.UpdatedAt is not null)
        {
            updatedAt = existingState.UpdatedAt;
            actions.Add("MergedMissingUpdatedAtFromExisting");
        }

        if (updatedAt is null)
        {
            updatedAt = now;
            actions.Add("InitializedMissingUpdatedAtToNow");
        }

        if (incomingState.RawUpdatedAt is not null && incomingState.UpdatedAtWasNormalized)
        {
            actions.Add("NormalizedUpdatedAtFromSeconds");
        }

        if (incomingState.RawUpdatedAt is not null && incomingState.UpdatedAtWasInvalid)
        {
            actions.Add("IgnoredInvalidIncomingUpdatedAt");
        }

        if (incomingFortuneWheel.ContainsKey("NextUpdateAt"))
        {
            incomingFortuneWheel.Remove("NextUpdateAt");
            actions.Add("RemovedClientNextUpdateAt");
        }

        var removedLegacy = false;
        removedLegacy |= incomingFortuneWheel.Remove("LastResetTimestamp");
        removedLegacy |= incomingFortuneWheel.Remove("lastResetTimestamp");
        removedLegacy |= incomingFortuneWheel.Remove("availableSpins");
        if (removedLegacy)
        {
            actions.Add("RemovedLegacyWheelKeys");
        }

        if (availableSpins is not null)
        {
            incomingFortuneWheel[AvailableSpinsPropertyName] = availableSpins.Value;
        }

        incomingFortuneWheel[UpdatedAtPropertyName] = updatedAt.Value;
    }

    private static WheelSaveReadResult ReadWheelState(JsonObject? wheelNode, long now)
    {
        if (wheelNode is null)
        {
            return new WheelSaveReadResult();
        }

        var availableSpins = TryGetInt(wheelNode, AvailableSpinsPropertyName);
        var rawUpdatedAt = TryGetLong(wheelNode, UpdatedAtPropertyName);
        long? updatedAt = null;
        var updatedAtWasNormalized = false;
        var updatedAtWasInvalid = false;
        if (rawUpdatedAt is not null)
        {
            if (TryNormalizeTimestamp(rawUpdatedAt.Value, now, out var normalizedUpdatedAt, out var wasNormalized))
            {
                updatedAt = normalizedUpdatedAt;
                updatedAtWasNormalized = wasNormalized;
            }
            else
            {
                updatedAtWasInvalid = true;
            }
        }

        return new WheelSaveReadResult
        {
            AvailableSpins = availableSpins,
            RawUpdatedAt = rawUpdatedAt,
            UpdatedAt = updatedAt,
            UpdatedAtWasNormalized = updatedAtWasNormalized,
            UpdatedAtWasInvalid = updatedAtWasInvalid
        };
    }

    private static bool TryNormalizeTimestamp(long rawTimestamp, long now, out long normalizedTimestamp, out bool wasNormalized)
    {
        normalizedTimestamp = rawTimestamp;
        wasNormalized = false;

        if (rawTimestamp <= 0)
        {
            return false;
        }

        if (rawTimestamp <= 9_999_999_999)
        {
            if (rawTimestamp > long.MaxValue / 1000)
            {
                return false;
            }

            var converted = rawTimestamp * 1000;
            if (converted > now)
            {
                return false;
            }

            normalizedTimestamp = converted;
            wasNormalized = true;
            return true;
        }

        if (rawTimestamp > now)
        {
            return false;
        }

        return true;
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

    private static void SanitizeBlobRoot(JsonObject root)
    {
        root.Remove(ResourcesPropertyName);
        root.Remove(ServerRewardsPropertyName);
        root.Remove(InventoryItemsPropertyName);
        root.Remove(InventoryPropertyName);
    }

    private static void ReplaceRootDocument(JsonObject destination, JsonObject source)
    {
        destination.Clear();
        foreach (var (key, value) in source)
        {
            destination[key] = value?.DeepClone();
        }
    }

    private static JsonObject? GetFortuneWheelObject(JsonObject? root)
    {
        if (root is null)
        {
            return null;
        }

        return root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode)
            ? fortuneWheelNode as JsonObject
            : null;
    }

    private static int? TryGetInt(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var propertyValue) || propertyValue is null)
        {
            return null;
        }

        if (propertyValue is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
        {
            return intValue;
        }

        if (propertyValue is JsonValue stringValue &&
            stringValue.TryGetValue(out string? raw) &&
            int.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? TryGetLong(JsonObject source, string propertyName)
    {
        if (!source.TryGetPropertyValue(propertyName, out var propertyValue) || propertyValue is null)
        {
            return null;
        }

        if (propertyValue is JsonValue jsonValue && jsonValue.TryGetValue(out long longValue))
        {
            return longValue;
        }

        if (propertyValue is JsonValue stringValue &&
            stringValue.TryGetValue(out string? raw) &&
            long.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private readonly struct WheelSaveReadResult
    {
        public int? AvailableSpins { get; init; }
        public long? RawUpdatedAt { get; init; }
        public long? UpdatedAt { get; init; }
        public bool UpdatedAtWasNormalized { get; init; }
        public bool UpdatedAtWasInvalid { get; init; }
    }
}
