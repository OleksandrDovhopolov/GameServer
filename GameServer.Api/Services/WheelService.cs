using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameServer.Api.Services;

public sealed class WheelService : IWheelService
{
    private const int MaxAvailableSpins = 8;
    private const string FortuneWheelPropertyName = "FortuneWheel";
    private const string AvailableSpinsPropertyName = "AvailableSpins";
    private const string UpdatedAtPropertyName = "UpdatedAt";
    private static readonly TimeSpan RegenInterval = TimeSpan.FromMinutes(15);
    private static readonly long RegenIntervalMs = (long)RegenInterval.TotalMilliseconds;

    private readonly ISaveRepository _saveRepository;
    private readonly IWheelRewardCatalog _rewardCatalog;
    private readonly ISystemClock _clock;
    private readonly ILogger<WheelService> _logger;

    public WheelService(ISaveRepository saveRepository, IWheelRewardCatalog rewardCatalog, ISystemClock clock)
        : this(saveRepository, rewardCatalog, clock, NullLogger<WheelService>.Instance)
    {
    }

    public WheelService(
        ISaveRepository saveRepository,
        IWheelRewardCatalog rewardCatalog,
        ISystemClock clock,
        ILogger<WheelService> logger)
    {
        _saveRepository = saveRepository;
        _rewardCatalog = rewardCatalog;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<WheelReward> GetRewards() => _rewardCatalog.GetRewards();

    public async Task<WheelStateResult> GetStateAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);

        var now = _clock.UtcNowUnixMilliseconds;
        _logger.LogInformation("Wheel GetState started. Now: {Now}, RegenIntervalMs: {RegenIntervalMs}", now, RegenIntervalMs);

        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        var hasExistingState = TryReadExistingState(root, now, out var state, out var diagnostics);
        var hasNonCanonicalWheelFields = HasNonCanonicalWheelFields(root);
        var stateChanged = hasNonCanonicalWheelFields ||
                           diagnostics.TimestampWasNormalized ||
                           diagnostics.TimestampWasRepaired;

        _logger.LogInformation(
            "Wheel GetState read outcome. HasExistingState: {HasExistingState}, Source: {Source}, RawAvailableSpins: {RawAvailableSpins}, RawUpdatedAt: {RawUpdatedAt}, NormalizedUpdatedAt: {NormalizedUpdatedAt}, TimestampWasNormalized: {TimestampWasNormalized}, TimestampWasRepaired: {TimestampWasRepaired}, FailureReason: {FailureReason}, HasNonCanonicalWheelFields: {HasNonCanonicalWheelFields}",
            hasExistingState,
            diagnostics.Source,
            diagnostics.RawAvailableSpins,
            diagnostics.RawUpdatedAt,
            diagnostics.NormalizedUpdatedAt,
            diagnostics.TimestampWasNormalized,
            diagnostics.TimestampWasRepaired,
            diagnostics.FailureReason,
            hasNonCanonicalWheelFields);

        if (hasNonCanonicalWheelFields)
        {
            _logger.LogWarning(
                "Wheel GetState detected non-canonical wheel fields. They will be ignored and canonicalized on save. AvailableSpins/UpdatedAt are the only persisted wheel keys.");
        }

        if (!hasExistingState)
        {
            state = new FortuneWheelModuleSaveData
            {
                AvailableSpins = MaxAvailableSpins,
                UpdatedAt = now,
                NextUpdateAt = now + RegenIntervalMs
            };
            stateChanged = true;
            _logger.LogWarning(
                "Wheel GetState initialized default state due to invalid or missing persisted state. AvailableSpins: {AvailableSpins}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}",
                state.AvailableSpins,
                state.UpdatedAt,
                state.NextUpdateAt);
        }

        var elapsedBeforeRegenMs = now - state.UpdatedAt;
        var regenApplied = ApplyRegenIfNeeded(state, now);
        stateChanged |= regenApplied;
        _logger.LogInformation(
            "Wheel GetState regen check. ElapsedMs: {ElapsedMs}, RegenThresholdMs: {RegenThresholdMs}, RegenApplied: {RegenApplied}, AvailableSpinsAfterRegen: {AvailableSpinsAfterRegen}, UpdatedAtAfterRegen: {UpdatedAtAfterRegen}, NextUpdateAtAfterRegen: {NextUpdateAtAfterRegen}",
            elapsedBeforeRegenMs,
            RegenIntervalMs,
            regenApplied,
            state.AvailableSpins,
            state.UpdatedAt,
            state.NextUpdateAt);

        if (stateChanged)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            _logger.LogInformation(
                "Wheel GetState persisted state. AvailableSpins: {AvailableSpins}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, SaveReasons: {SaveReasons}",
                state.AvailableSpins,
                state.UpdatedAt,
                state.NextUpdateAt,
                BuildSaveReasons(
                    hasNonCanonicalWheelFields,
                    diagnostics.TimestampWasNormalized,
                    diagnostics.TimestampWasRepaired,
                    regenApplied,
                    !hasExistingState));
        }
        else
        {
            _logger.LogInformation("Wheel GetState did not persist because state is unchanged.");
        }

        return new WheelStateResult
        {
            AvailableSpins = state.AvailableSpins,
            UpdatedAt = state.UpdatedAt,
            NextUpdateAt = state.NextUpdateAt
        };
    }

    public async Task<WheelSpinResult> SpinAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);

        var now = _clock.UtcNowUnixMilliseconds;
        _logger.LogInformation("Wheel Spin started. Now: {Now}, RegenIntervalMs: {RegenIntervalMs}", now, RegenIntervalMs);

        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        var state = ReadState(root, now, out var hasExistingState, out var diagnostics);
        var hasNonCanonicalWheelFields = HasNonCanonicalWheelFields(root);
        _logger.LogInformation(
            "Wheel Spin read outcome. HasExistingState: {HasExistingState}, Source: {Source}, RawAvailableSpins: {RawAvailableSpins}, RawUpdatedAt: {RawUpdatedAt}, NormalizedUpdatedAt: {NormalizedUpdatedAt}, TimestampWasNormalized: {TimestampWasNormalized}, TimestampWasRepaired: {TimestampWasRepaired}, FailureReason: {FailureReason}, HasNonCanonicalWheelFields: {HasNonCanonicalWheelFields}, EffectiveAvailableSpins: {EffectiveAvailableSpins}, EffectiveUpdatedAt: {EffectiveUpdatedAt}, EffectiveNextUpdateAt: {EffectiveNextUpdateAt}",
            hasExistingState,
            diagnostics.Source,
            diagnostics.RawAvailableSpins,
            diagnostics.RawUpdatedAt,
            diagnostics.NormalizedUpdatedAt,
            diagnostics.TimestampWasNormalized,
            diagnostics.TimestampWasRepaired,
            diagnostics.FailureReason,
            hasNonCanonicalWheelFields,
            state.AvailableSpins,
            state.UpdatedAt,
            state.NextUpdateAt);

        if (hasNonCanonicalWheelFields)
        {
            _logger.LogWarning(
                "Wheel Spin detected non-canonical wheel fields. They will be ignored and canonicalized on save. AvailableSpins/UpdatedAt are the only persisted wheel keys.");
        }

        var spinsBeforeRegen = state.AvailableSpins;
        var updatedAtBeforeRegen = state.UpdatedAt;
        var elapsedBeforeRegenMs = now - state.UpdatedAt;
        var regenApplied = ApplyRegenIfNeeded(state, now);
        _logger.LogInformation(
            "Wheel Spin regen check. ElapsedMs: {ElapsedMs}, RegenThresholdMs: {RegenThresholdMs}, RegenApplied: {RegenApplied}, AvailableSpinsBeforeRegen: {AvailableSpinsBeforeRegen}, AvailableSpinsAfterRegen: {AvailableSpinsAfterRegen}, UpdatedAtBeforeRegen: {UpdatedAtBeforeRegen}, UpdatedAtAfterRegen: {UpdatedAtAfterRegen}, NextUpdateAtAfterRegen: {NextUpdateAtAfterRegen}",
            elapsedBeforeRegenMs,
            RegenIntervalMs,
            regenApplied,
            spinsBeforeRegen,
            state.AvailableSpins,
            updatedAtBeforeRegen,
            state.UpdatedAt,
            state.NextUpdateAt);

        if (state.AvailableSpins <= 0)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            _logger.LogWarning(
                "Wheel Spin blocked due to no spins. UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, PersistedAvailableSpins: {PersistedAvailableSpins}",
                state.UpdatedAt,
                state.NextUpdateAt,
                state.AvailableSpins);
            throw new NoSpinsAvailableException(state.UpdatedAt, state.NextUpdateAt);
        }

        var spinsBeforeConsume = state.AvailableSpins;
        var rewardId = _rewardCatalog.RollRewardId();
        state.AvailableSpins -= 1;
        state.NextUpdateAt = CalculateNextUpdateTimestamp(state.UpdatedAt);
        _logger.LogInformation(
            "Wheel Spin consumed one spin. RewardId: {RewardId}, AvailableSpinsBeforeConsume: {AvailableSpinsBeforeConsume}, AvailableSpinsAfterConsume: {AvailableSpinsAfterConsume}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}",
            rewardId,
            spinsBeforeConsume,
            state.AvailableSpins,
            state.UpdatedAt,
            state.NextUpdateAt);

        SetState(root, state);
        await SaveDocumentAsync(playerId, root, cancellationToken);
        _logger.LogInformation(
            "Wheel Spin persisted state. AvailableSpins: {AvailableSpins}, UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}",
            state.AvailableSpins,
            state.UpdatedAt,
            state.NextUpdateAt);

        return new WheelSpinResult
        {
            RewardId = rewardId,
            AvailableSpins = state.AvailableSpins,
            UpdatedAt = state.UpdatedAt,
            NextUpdateAt = state.NextUpdateAt
        };
    }

    private async Task<JsonObject> LoadSaveDocumentAsync(string playerId, CancellationToken cancellationToken)
    {
        var save = await _saveRepository.LoadAsync(playerId, cancellationToken);
        if (save is null || string.IsNullOrWhiteSpace(save.Data))
        {
            _logger.LogInformation("Wheel save document is empty or missing.");
            return [];
        }

        try
        {
            var parsed = JsonNode.Parse(save.Data);
            if (parsed is JsonObject root)
            {
                _logger.LogInformation(
                    "Wheel save document loaded. PayloadLength: {PayloadLength}, RootKeys: {RootKeys}",
                    save.Data.Length,
                    string.Join(", ", root.Select(kvp => kvp.Key)));
                return root;
            }

            _logger.LogWarning("Wheel save document root is not a JSON object. Resetting to empty object.");
            return [];
        }
        catch
        {
            _logger.LogWarning("Wheel save document JSON parse failed. Resetting to empty object.");
            return [];
        }
    }

    private async Task SaveDocumentAsync(string playerId, JsonObject document, CancellationToken cancellationToken)
    {
        var payload = document.ToJsonString();
        await _saveRepository.SaveAsync(playerId, payload, cancellationToken);
        _logger.LogInformation(
            "Wheel save document persisted. PayloadLength: {PayloadLength}, RootKeys: {RootKeys}",
            payload.Length,
            string.Join(", ", document.Select(kvp => kvp.Key)));
    }

    private static FortuneWheelModuleSaveData ReadState(
        JsonObject root,
        long now,
        out bool hasExistingState,
        out WheelStateReadDiagnostics diagnostics)
    {
        hasExistingState = TryReadExistingState(root, now, out var state, out diagnostics);
        if (hasExistingState)
        {
            return state;
        }

        return new FortuneWheelModuleSaveData
        {
            AvailableSpins = MaxAvailableSpins,
            UpdatedAt = now,
            NextUpdateAt = now + RegenIntervalMs
        };
    }

    private static bool TryReadExistingState(
        JsonObject root,
        long now,
        out FortuneWheelModuleSaveData state,
        out WheelStateReadDiagnostics diagnostics)
    {
        if (!root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode))
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = "FortuneWheelMissing"
            };
            return false;
        }

        if (fortuneWheelNode is not JsonObject fortuneWheel)
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = "FortuneWheelIsNotJsonObject"
            };
            return false;
        }

        return TryReadStateFromFortuneWheel(fortuneWheel, now, out state, out diagnostics);
    }

    private static bool TryReadStateFromFortuneWheel(
        JsonObject fortuneWheel,
        long now,
        out FortuneWheelModuleSaveData state,
        out WheelStateReadDiagnostics diagnostics)
    {
        var availableSpins = TryGetInt(fortuneWheel, AvailableSpinsPropertyName);
        var rawUpdatedAt = TryGetLong(fortuneWheel, UpdatedAtPropertyName);

        if (availableSpins is null)
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = "MissingOrInvalidAvailableSpins",
                RawUpdatedAt = rawUpdatedAt
            };
            return false;
        }

        if (availableSpins < 0 || availableSpins > MaxAvailableSpins)
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = "AvailableSpinsOutOfRange",
                RawAvailableSpins = availableSpins,
                RawUpdatedAt = rawUpdatedAt
            };
            return false;
        }

        if (rawUpdatedAt is null)
        {
            state = new FortuneWheelModuleSaveData
            {
                AvailableSpins = availableSpins.Value,
                UpdatedAt = now,
                NextUpdateAt = now + RegenIntervalMs
            };
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = "MissingUpdatedAtRepairedToNow",
                RawAvailableSpins = availableSpins,
                TimestampWasRepaired = true,
                NormalizedUpdatedAt = now
            };
            return true;
        }

        if (!TryNormalizeTimestamp(rawUpdatedAt.Value, now, out var normalizedTimestamp, out var timestampWasNormalized, out var timestampFailureReason))
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = "FortuneWheel",
                FailureReason = timestampFailureReason ?? "UpdatedAtInvalid",
                RawAvailableSpins = availableSpins,
                RawUpdatedAt = rawUpdatedAt
            };
            return false;
        }

        state = new FortuneWheelModuleSaveData
        {
            AvailableSpins = availableSpins.Value,
            UpdatedAt = normalizedTimestamp,
            NextUpdateAt = CalculateNextUpdateTimestamp(normalizedTimestamp)
        };
        diagnostics = new WheelStateReadDiagnostics
        {
            Source = "FortuneWheel",
            TimestampWasNormalized = timestampWasNormalized,
            RawAvailableSpins = availableSpins,
            RawUpdatedAt = rawUpdatedAt,
            NormalizedUpdatedAt = normalizedTimestamp
        };
        return true;
    }

    private static bool TryNormalizeTimestamp(
        long rawTimestamp,
        long now,
        out long normalizedTimestamp,
        out bool timestampWasNormalized,
        out string? failureReason)
    {
        normalizedTimestamp = rawTimestamp;
        timestampWasNormalized = false;
        failureReason = null;

        if (rawTimestamp <= 0)
        {
            failureReason = "UpdatedAtNonPositive";
            return false;
        }

        if (LooksLikeUnixSeconds(rawTimestamp))
        {
            if (rawTimestamp > long.MaxValue / 1000)
            {
                failureReason = "UpdatedAtSecondsOverflow";
                return false;
            }

            normalizedTimestamp = rawTimestamp * 1000;
            timestampWasNormalized = true;
            if (normalizedTimestamp <= now)
            {
                return true;
            }

            failureReason = "UpdatedAtSecondsInFutureAfterNormalization";
            return false;
        }

        if (rawTimestamp <= now)
        {
            return true;
        }

        failureReason = "UpdatedAtMillisecondsInFuture";
        return false;
    }

    private static bool LooksLikeUnixSeconds(long timestamp)
    {
        return timestamp <= 9_999_999_999;
    }

    private static bool ApplyRegenIfNeeded(FortuneWheelModuleSaveData state, long now)
    {
        if (now - state.UpdatedAt < RegenIntervalMs)
        {
            state.NextUpdateAt = CalculateNextUpdateTimestamp(state.UpdatedAt);
            return false;
        }

        state.AvailableSpins = MaxAvailableSpins;
        state.UpdatedAt = now;
        state.NextUpdateAt = CalculateNextUpdateTimestamp(state.UpdatedAt);
        return true;
    }

    private static long CalculateNextUpdateTimestamp(long updatedAt)
    {
        return updatedAt + RegenIntervalMs;
    }

    private static bool HasNonCanonicalWheelFields(JsonObject root)
    {
        if (root.ContainsKey("availableSpins") || root.ContainsKey("lastResetTimestamp"))
        {
            return true;
        }

        if (!root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode) || fortuneWheelNode is not JsonObject fortuneWheel)
        {
            return false;
        }

        return fortuneWheel.Any(kvp => kvp.Key != AvailableSpinsPropertyName && kvp.Key != UpdatedAtPropertyName);
    }

    private static string BuildSaveReasons(
        bool hasNonCanonicalWheelFields,
        bool timestampWasNormalized,
        bool timestampWasRepaired,
        bool regenApplied,
        bool initializedDefaults)
    {
        var reasons = new List<string>();
        if (initializedDefaults)
        {
            reasons.Add("InitializedDefaults");
        }

        if (timestampWasNormalized)
        {
            reasons.Add("NormalizedUpdatedAt");
        }

        if (timestampWasRepaired)
        {
            reasons.Add("RepairedMissingUpdatedAt");
        }

        if (regenApplied)
        {
            reasons.Add("AppliedRegen");
        }

        if (hasNonCanonicalWheelFields)
        {
            reasons.Add("RemovedNonCanonicalWheelFields");
        }

        return reasons.Count == 0 ? "None" : string.Join(", ", reasons);
    }

    private static void SetState(JsonObject root, FortuneWheelModuleSaveData state)
    {
        root.Remove("availableSpins");
        root.Remove("lastResetTimestamp");

        JsonObject fortuneWheel;
        if (root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode) && fortuneWheelNode is JsonObject existingFortuneWheel)
        {
            fortuneWheel = existingFortuneWheel;
        }
        else
        {
            fortuneWheel = [];
            root[FortuneWheelPropertyName] = fortuneWheel;
        }

        state.NextUpdateAt = CalculateNextUpdateTimestamp(state.UpdatedAt);

        fortuneWheel[AvailableSpinsPropertyName] = state.AvailableSpins;
        fortuneWheel[UpdatedAtPropertyName] = state.UpdatedAt;

        // Canonical wheel payload is persisted with only AvailableSpins + UpdatedAt.
        fortuneWheel.Remove("NextUpdateAt");
        fortuneWheel.Remove("LastResetTimestamp");
        fortuneWheel.Remove("lastResetTimestamp");
        fortuneWheel.Remove("availableSpins");
    }

    private static int? TryGetInt(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue(out int intValue))
        {
            return intValue;
        }

        if (value is JsonValue stringValue && stringValue.TryGetValue(out string? raw) && int.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? TryGetLong(JsonObject root, string propertyName)
    {
        if (!root.TryGetPropertyValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue(out long longValue))
        {
            return longValue;
        }

        if (value is JsonValue stringValue && stringValue.TryGetValue(out string? raw) && long.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static void ValidatePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("PlayerId is required.", nameof(playerId));
        }
    }

    private readonly struct WheelStateReadDiagnostics
    {
        public string Source { get; init; }
        public bool TimestampWasNormalized { get; init; }
        public bool TimestampWasRepaired { get; init; }
        public int? RawAvailableSpins { get; init; }
        public long? RawUpdatedAt { get; init; }
        public long? NormalizedUpdatedAt { get; init; }
        public string? FailureReason { get; init; }
    }
}
