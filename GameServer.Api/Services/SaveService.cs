using System.Text;
using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameServer.Api.Services;

public sealed class SaveService : ISaveService
{
    private const int MaxSaveBytes = 30 * 1024;
    private const string FortuneWheelPropertyName = "FortuneWheel";
    private const string AvailableSpinsPropertyName = "AvailableSpins";
    private const string LastResetTimestampPropertyName = "LastResetTimestamp";
    private const string LegacyNestedAvailableSpinsPropertyName = "availableSpins";
    private const string LegacyNestedLastResetTimestampPropertyName = "lastResetTimestamp";

    private readonly ISaveRepository _saveRepository;
    private readonly ISystemClock _clock;
    private readonly ILogger<SaveService> _logger;

    public SaveService(ISaveRepository saveRepository)
        : this(saveRepository, new SystemClock(), NullLogger<SaveService>.Instance)
    {
    }

    public SaveService(ISaveRepository saveRepository, ISystemClock clock, ILogger<SaveService> logger)
    {
        _saveRepository = saveRepository;
        _clock = clock;
        _logger = logger;
    }

    public async Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        ValidateData(data);

        var guardedData = await ApplyWheelSaveGuardsAsync(playerId, data, cancellationToken);
        await _saveRepository.SaveAsync(playerId, guardedData, cancellationToken);
    }

    public async Task<SaveRecord> LoadAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var save = await _saveRepository.LoadAsync(playerId, cancellationToken);
        return save ?? throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
    }

    public async Task DeleteAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);
        var exists = await _saveRepository.ExistsAsync(playerId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Save for player '{playerId}' was not found.");
        }

        await _saveRepository.DeleteAsync(playerId, cancellationToken);
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
        if (!TryGetInt(incomingFortuneWheel, AvailableSpinsPropertyName, LegacyNestedAvailableSpinsPropertyName, out var availableSpins))
        {
            if (TryGetInt(existingFortuneWheel, AvailableSpinsPropertyName, LegacyNestedAvailableSpinsPropertyName, out var existingSpins))
            {
                availableSpins = existingSpins;
                actions.Add("MergedMissingAvailableSpinsFromExisting");
            }
        }

        if (availableSpins is not null)
        {
            incomingFortuneWheel[AvailableSpinsPropertyName] = availableSpins.Value;
        }

        if (!TryGetLong(incomingFortuneWheel, LastResetTimestampPropertyName, LegacyNestedLastResetTimestampPropertyName, out var lastResetTimestamp))
        {
            if (TryGetLong(existingFortuneWheel, LastResetTimestampPropertyName, LegacyNestedLastResetTimestampPropertyName, out var existingTimestamp))
            {
                lastResetTimestamp = existingTimestamp;
                actions.Add("MergedMissingTimestampFromExisting");
            }
            else
            {
                lastResetTimestamp = _clock.UtcNowUnixMilliseconds;
                actions.Add("InitializedMissingTimestampToNow");
            }
        }

        var canonicalTimestamp = lastResetTimestamp ?? _clock.UtcNowUnixMilliseconds;
        incomingFortuneWheel[LastResetTimestampPropertyName] = canonicalTimestamp;
        incomingFortuneWheel.Remove(LegacyNestedAvailableSpinsPropertyName);
        incomingFortuneWheel.Remove(LegacyNestedLastResetTimestampPropertyName);
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

    private static bool TryGetInt(JsonObject? source, string primaryProperty, string fallbackProperty, out int? value)
    {
        value = source is null ? null : TryGetInt(source, primaryProperty);
        if (value is not null)
        {
            return true;
        }

        value = source is null ? null : TryGetInt(source, fallbackProperty);
        return value is not null;
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

    private static bool TryGetLong(JsonObject? source, string primaryProperty, string fallbackProperty, out long? value)
    {
        value = source is null ? null : TryGetLong(source, primaryProperty);
        if (value is not null)
        {
            return true;
        }

        value = source is null ? null : TryGetLong(source, fallbackProperty);
        return value is not null;
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
}
