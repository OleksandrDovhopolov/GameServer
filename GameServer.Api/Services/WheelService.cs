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
    private const string LastResetTimestampPropertyName = "LastResetTimestamp";
    private const string LegacyNestedAvailableSpinsPropertyName = "availableSpins";
    private const string LegacyNestedLastResetTimestampPropertyName = "lastResetTimestamp";
    private const string LegacyAvailableSpinsPropertyName = "availableSpins";
    private const string LegacyLastResetTimestampPropertyName = "lastResetTimestamp";
    private static readonly TimeSpan RegenInterval = TimeSpan.FromMinutes(2);
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
        var hasLegacyRootFields = HasLegacyRootWheelFields(root);
        var stateChanged = hasLegacyRootFields ||
                           diagnostics.TimestampWasNormalized ||
                           diagnostics.TimestampWasRepaired ||
                           diagnostics.UsedLegacyNestedTimestampKey;

        _logger.LogInformation(
            "Wheel GetState read outcome. HasExistingState: {HasExistingState}, Source: {Source}, RawAvailableSpins: {RawAvailableSpins}, RawLastResetTimestamp: {RawLastResetTimestamp}, NormalizedLastResetTimestamp: {NormalizedLastResetTimestamp}, TimestampWasNormalized: {TimestampWasNormalized}, TimestampWasRepaired: {TimestampWasRepaired}, UsedLegacyNestedTimestampKey: {UsedLegacyNestedTimestampKey}, FailureReason: {FailureReason}, HasLegacyRootFields: {HasLegacyRootFields}",
            hasExistingState,
            diagnostics.Source,
            diagnostics.RawAvailableSpins,
            diagnostics.RawLastResetTimestamp,
            diagnostics.NormalizedLastResetTimestamp,
            diagnostics.TimestampWasNormalized,
            diagnostics.TimestampWasRepaired,
            diagnostics.UsedLegacyNestedTimestampKey,
            diagnostics.FailureReason,
            hasLegacyRootFields);

        if (!hasExistingState)
        {
            state = new WheelState
            {
                AvailableSpins = MaxAvailableSpins,
                LastResetTimestamp = now
            };
            stateChanged = true;
            _logger.LogWarning(
                "Wheel GetState initialized default state due to invalid or missing persisted state. AvailableSpins: {AvailableSpins}, LastResetTimestamp: {LastResetTimestamp}",
                state.AvailableSpins,
                state.LastResetTimestamp);
        }

        var elapsedBeforeRegenMs = now - state.LastResetTimestamp;
        var regenApplied = ApplyRegenIfNeeded(ref state, now);
        stateChanged |= regenApplied;
        _logger.LogInformation(
            "Wheel GetState regen check. ElapsedMs: {ElapsedMs}, RegenThresholdMs: {RegenThresholdMs}, RegenApplied: {RegenApplied}, AvailableSpinsAfterRegen: {AvailableSpinsAfterRegen}, LastResetTimestampAfterRegen: {LastResetTimestampAfterRegen}",
            elapsedBeforeRegenMs,
            RegenIntervalMs,
            regenApplied,
            state.AvailableSpins,
            state.LastResetTimestamp);

        if (stateChanged)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            _logger.LogInformation(
                "Wheel GetState persisted state. AvailableSpins: {AvailableSpins}, LastResetTimestamp: {LastResetTimestamp}, SaveReasons: {SaveReasons}",
                state.AvailableSpins,
                state.LastResetTimestamp,
                BuildSaveReasons(
                    hasLegacyRootFields,
                    diagnostics.TimestampWasNormalized,
                    diagnostics.TimestampWasRepaired,
                    diagnostics.UsedLegacyNestedTimestampKey,
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
            UpdatedAt = state.LastResetTimestamp,
            NextUpdateAt = CalculateNextUpdateTimestamp(state)
        };
    }

    public async Task<WheelSpinResult> SpinAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);

        var now = _clock.UtcNowUnixMilliseconds;
        _logger.LogInformation("Wheel Spin started. Now: {Now}, RegenIntervalMs: {RegenIntervalMs}", now, RegenIntervalMs);

        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        var state = ReadState(root, now, out var hasExistingState, out var diagnostics);
        _logger.LogInformation(
            "Wheel Spin read outcome. HasExistingState: {HasExistingState}, Source: {Source}, RawAvailableSpins: {RawAvailableSpins}, RawLastResetTimestamp: {RawLastResetTimestamp}, NormalizedLastResetTimestamp: {NormalizedLastResetTimestamp}, TimestampWasNormalized: {TimestampWasNormalized}, TimestampWasRepaired: {TimestampWasRepaired}, UsedLegacyNestedTimestampKey: {UsedLegacyNestedTimestampKey}, FailureReason: {FailureReason}, EffectiveAvailableSpins: {EffectiveAvailableSpins}, EffectiveLastResetTimestamp: {EffectiveLastResetTimestamp}",
            hasExistingState,
            diagnostics.Source,
            diagnostics.RawAvailableSpins,
            diagnostics.RawLastResetTimestamp,
            diagnostics.NormalizedLastResetTimestamp,
            diagnostics.TimestampWasNormalized,
            diagnostics.TimestampWasRepaired,
            diagnostics.UsedLegacyNestedTimestampKey,
            diagnostics.FailureReason,
            state.AvailableSpins,
            state.LastResetTimestamp);

        var spinsBeforeRegen = state.AvailableSpins;
        var lastResetBeforeRegen = state.LastResetTimestamp;
        var elapsedBeforeRegenMs = now - state.LastResetTimestamp;
        var regenApplied = ApplyRegenIfNeeded(ref state, now);
        _logger.LogInformation(
            "Wheel Spin regen check. ElapsedMs: {ElapsedMs}, RegenThresholdMs: {RegenThresholdMs}, RegenApplied: {RegenApplied}, AvailableSpinsBeforeRegen: {AvailableSpinsBeforeRegen}, AvailableSpinsAfterRegen: {AvailableSpinsAfterRegen}, LastResetTimestampBeforeRegen: {LastResetTimestampBeforeRegen}, LastResetTimestampAfterRegen: {LastResetTimestampAfterRegen}",
            elapsedBeforeRegenMs,
            RegenIntervalMs,
            regenApplied,
            spinsBeforeRegen,
            state.AvailableSpins,
            lastResetBeforeRegen,
            state.LastResetTimestamp);

        if (state.AvailableSpins <= 0)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            _logger.LogWarning(
                "Wheel Spin blocked due to no spins. UpdatedAt: {UpdatedAt}, NextUpdateAt: {NextUpdateAt}, PersistedAvailableSpins: {PersistedAvailableSpins}, PersistedLastResetTimestamp: {PersistedLastResetTimestamp}",
                state.LastResetTimestamp,
                CalculateNextUpdateTimestamp(state),
                state.AvailableSpins,
                state.LastResetTimestamp);
            throw new NoSpinsAvailableException(state.LastResetTimestamp, CalculateNextUpdateTimestamp(state));
        }

        var spinsBeforeConsume = state.AvailableSpins;
        var rewardId = _rewardCatalog.RollRewardId();
        state.AvailableSpins -= 1;
        _logger.LogInformation(
            "Wheel Spin consumed one spin. RewardId: {RewardId}, AvailableSpinsBeforeConsume: {AvailableSpinsBeforeConsume}, AvailableSpinsAfterConsume: {AvailableSpinsAfterConsume}",
            rewardId,
            spinsBeforeConsume,
            state.AvailableSpins);

        SetState(root, state);
        await SaveDocumentAsync(playerId, root, cancellationToken);
        _logger.LogInformation(
            "Wheel Spin persisted state. AvailableSpins: {AvailableSpins}, LastResetTimestamp: {LastResetTimestamp}, NextUpdateAt: {NextUpdateAt}",
            state.AvailableSpins,
            state.LastResetTimestamp,
            CalculateNextUpdateTimestamp(state));

        return new WheelSpinResult
        {
            RewardId = rewardId,
            AvailableSpins = state.AvailableSpins,
            UpdatedAt = state.LastResetTimestamp,
            NextUpdateAt = CalculateNextUpdateTimestamp(state)
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

    private static WheelState ReadState(JsonObject root, long now, out bool hasExistingState, out WheelStateReadDiagnostics diagnostics)
    {
        hasExistingState = TryReadExistingState(root, now, out var state, out diagnostics);
        if (hasExistingState)
        {
            return state;
        }

        return new WheelState
        {
            AvailableSpins = MaxAvailableSpins,
            LastResetTimestamp = now
        };
    }

    private static bool TryReadExistingState(JsonObject root, long now, out WheelState state, out WheelStateReadDiagnostics diagnostics)
    {
        if (root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode))
        {
            if (fortuneWheelNode is not JsonObject fortuneWheel)
            {
                state = default;
                diagnostics = new WheelStateReadDiagnostics
                {
                    Source = "FortuneWheel",
                    FailureReason = "FortuneWheelIsNotJsonObject"
                };
                return false;
            }

            return TryReadStateFromNode(
                fortuneWheel,
                AvailableSpinsPropertyName,
                LastResetTimestampPropertyName,
                "FortuneWheel",
                now,
                out state,
                out diagnostics);
        }

        return TryReadStateFromNode(
            root,
            LegacyAvailableSpinsPropertyName,
            LegacyLastResetTimestampPropertyName,
            "LegacyRoot",
            now,
            out state,
            out diagnostics);
    }

    private static bool TryReadStateFromNode(
        JsonObject sourceNode,
        string availableSpinsPropertyName,
        string lastResetTimestampPropertyName,
        string source,
        long now,
        out WheelState state,
        out WheelStateReadDiagnostics diagnostics)
    {
        var availableSpins = TryGetInt(sourceNode, availableSpinsPropertyName);
        var rawLastResetTimestamp = TryGetLong(sourceNode, lastResetTimestampPropertyName);
        var usedLegacyNestedTimestampKey = false;
        if (rawLastResetTimestamp is null && source == "FortuneWheel")
        {
            rawLastResetTimestamp = TryGetLong(sourceNode, LegacyNestedLastResetTimestampPropertyName);
            usedLegacyNestedTimestampKey = rawLastResetTimestamp is not null;
        }

        if (availableSpins is null)
        {
            state = default;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = source,
                FailureReason = "MissingOrInvalidAvailableSpins",
                RawLastResetTimestamp = rawLastResetTimestamp
            };
            return false;
        }

        if (availableSpins < 0 || availableSpins > MaxAvailableSpins)
        {
            state = default;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = source,
                FailureReason = "AvailableSpinsOutOfRange",
                RawAvailableSpins = availableSpins,
                RawLastResetTimestamp = rawLastResetTimestamp
            };
            return false;
        }

        if (rawLastResetTimestamp is null)
        {
            state = new WheelState
            {
                AvailableSpins = availableSpins.Value,
                LastResetTimestamp = now
            };
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = source,
                FailureReason = "MissingLastResetTimestampRepairedToNow",
                RawAvailableSpins = availableSpins,
                TimestampWasRepaired = true,
                NormalizedLastResetTimestamp = now,
                UsedLegacyNestedTimestampKey = usedLegacyNestedTimestampKey
            };
            return true;
        }

        if (!TryNormalizeTimestamp(rawLastResetTimestamp.Value, now, out var normalizedTimestamp, out var timestampWasNormalized, out var timestampFailureReason))
        {
            state = default;
            diagnostics = new WheelStateReadDiagnostics
            {
                Source = source,
                FailureReason = timestampFailureReason ?? "LastResetTimestampInvalid",
                RawAvailableSpins = availableSpins,
                RawLastResetTimestamp = rawLastResetTimestamp,
                UsedLegacyNestedTimestampKey = usedLegacyNestedTimestampKey
            };
            return false;
        }

        state = new WheelState
        {
            AvailableSpins = availableSpins.Value,
            LastResetTimestamp = normalizedTimestamp
        };
        diagnostics = new WheelStateReadDiagnostics
        {
            Source = source,
            TimestampWasNormalized = timestampWasNormalized,
            RawAvailableSpins = availableSpins,
            RawLastResetTimestamp = rawLastResetTimestamp,
            NormalizedLastResetTimestamp = normalizedTimestamp,
            UsedLegacyNestedTimestampKey = usedLegacyNestedTimestampKey
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
            failureReason = "LastResetTimestampNonPositive";
            return false;
        }

        if (LooksLikeUnixSeconds(rawTimestamp))
        {
            if (rawTimestamp > long.MaxValue / 1000)
            {
                failureReason = "LastResetTimestampSecondsOverflow";
                return false;
            }

            normalizedTimestamp = rawTimestamp * 1000;
            timestampWasNormalized = true;
            if (normalizedTimestamp <= now)
            {
                return true;
            }

            failureReason = "LastResetTimestampSecondsInFutureAfterNormalization";
            return false;
        }

        if (rawTimestamp <= now)
        {
            return true;
        }

        failureReason = "LastResetTimestampMillisecondsInFuture";
        return false;
    }

    private static bool LooksLikeUnixSeconds(long timestamp)
    {
        return timestamp <= 9_999_999_999;
    }

    private static bool ApplyRegenIfNeeded(ref WheelState state, long now)
    {
        if (now - state.LastResetTimestamp < RegenIntervalMs)
        {
            return false;
        }

        state.AvailableSpins = MaxAvailableSpins;
        state.LastResetTimestamp = now;
        return true;
    }

    private static long CalculateNextUpdateTimestamp(WheelState state)
    {
        return state.LastResetTimestamp + RegenIntervalMs;
    }

    private static bool HasLegacyRootWheelFields(JsonObject root)
    {
        return root.ContainsKey(LegacyAvailableSpinsPropertyName) || root.ContainsKey(LegacyLastResetTimestampPropertyName);
    }

    private static string BuildSaveReasons(
        bool hasLegacyRootFields,
        bool timestampWasNormalized,
        bool timestampWasRepaired,
        bool usedLegacyNestedTimestampKey,
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
            reasons.Add("NormalizedTimestamp");
        }

        if (timestampWasRepaired)
        {
            reasons.Add("RepairedMissingTimestamp");
        }

        if (usedLegacyNestedTimestampKey)
        {
            reasons.Add("CanonicalizedLegacyNestedTimestampKey");
        }

        if (regenApplied)
        {
            reasons.Add("AppliedRegen");
        }

        if (hasLegacyRootFields)
        {
            reasons.Add("RemovedLegacyRootFields");
        }

        return reasons.Count == 0 ? "None" : string.Join(", ", reasons);
    }

    private static void SetState(JsonObject root, WheelState state)
    {
        root.Remove(LegacyAvailableSpinsPropertyName);
        root.Remove(LegacyLastResetTimestampPropertyName);

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

        fortuneWheel[AvailableSpinsPropertyName] = state.AvailableSpins;
        fortuneWheel[LastResetTimestampPropertyName] = state.LastResetTimestamp;
        fortuneWheel.Remove(LegacyNestedAvailableSpinsPropertyName);
        fortuneWheel.Remove(LegacyNestedLastResetTimestampPropertyName);
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
        public bool UsedLegacyNestedTimestampKey { get; init; }
        public int? RawAvailableSpins { get; init; }
        public long? RawLastResetTimestamp { get; init; }
        public long? NormalizedLastResetTimestamp { get; init; }
        public string? FailureReason { get; init; }
    }

    private struct WheelState
    {
        public int AvailableSpins { get; set; }
        public long LastResetTimestamp { get; set; }
    }
}
