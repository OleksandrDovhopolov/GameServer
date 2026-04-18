using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameServer.Api.Services;

public sealed class WheelService : IWheelService
{
    private const int MaxAvailableSpins = 8;
    private const string WheelRewardSource = "wheel";
    private const string FortuneWheelPropertyName = "FortuneWheel";
    private const string AvailableSpinsPropertyName = "AvailableSpins";
    private const string UpdatedAtPropertyName = "UpdatedAt";
    private static readonly TimeSpan RegenInterval = TimeSpan.FromMinutes(2);
    private static readonly long RegenIntervalMs = (long)RegenInterval.TotalMilliseconds;

    private readonly ISaveRepository _saveRepository;
    private readonly IWheelRewardCatalog _rewardCatalog;
    private readonly ISystemClock _clock;
    private readonly IRewardDefinitionRepository _rewardDefinitionRepository;
    private readonly IRewardPartApplier _rewardPartApplier;
    private readonly ILogger<WheelService> _logger;

    public WheelService(ISaveRepository saveRepository, IWheelRewardCatalog rewardCatalog, ISystemClock clock)
        : this(
            saveRepository,
            rewardCatalog,
            clock,
            new EmptyRewardDefinitionRepository(),
            new NoOpRewardPartApplier(),
            NullLogger<WheelService>.Instance)
    {
    }

    public WheelService(
        ISaveRepository saveRepository,
        IWheelRewardCatalog rewardCatalog,
        ISystemClock clock,
        IRewardDefinitionRepository rewardDefinitionRepository,
        IRewardPartApplier rewardPartApplier,
        ILogger<WheelService> logger)
    {
        _saveRepository = saveRepository;
        _rewardCatalog = rewardCatalog;
        _clock = clock;
        _rewardDefinitionRepository = rewardDefinitionRepository;
        _rewardPartApplier = rewardPartApplier;
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

        if (!hasExistingState)
        {
            state = new FortuneWheelModuleSaveData
            {
                AvailableSpins = MaxAvailableSpins,
                UpdatedAt = now,
                NextUpdateAt = now + RegenIntervalMs
            };
            stateChanged = true;
        }

        var regenApplied = ApplyRegenIfNeeded(state, now);
        stateChanged |= regenApplied;

        if (stateChanged)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
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
        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        var state = ReadState(root, now, out _, out _);
        _ = ApplyRegenIfNeeded(state, now);

        if (state.AvailableSpins <= 0)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            throw new NoSpinsAvailableException(state.UpdatedAt, state.NextUpdateAt);
        }

        var rewardId = _rewardCatalog.RollRewardId();
        state.AvailableSpins -= 1;
        state.NextUpdateAt = CalculateNextUpdateTimestamp(state.UpdatedAt);

        var rewardDefinition = await _rewardDefinitionRepository.GetByIdAsync(rewardId, cancellationToken);
        if (rewardDefinition is null)
        {
            throw new InvalidOperationException($"Wheel reward definition '{rewardId}' was not found.");
        }

        if (!string.Equals(rewardDefinition.Source, WheelRewardSource, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Wheel reward definition '{rewardId}' source must be '{WheelRewardSource}', but was '{rewardDefinition.Source}'.");
        }

        var player = PlayerAggregateDocument.Deserialize(playerId, root.ToJsonString());
        await _rewardPartApplier.ApplyAsync(player, rewardDefinition, cancellationToken);
        PlayerAggregateDocument.WriteBack(player);
        root = player.RootDocument;

        var rewardGrantResponse = new GrantRewardResponse
        {
            Success = true,
            RewardId = rewardId,
            PlayerState = PlayerAggregateDocument.ToSnapshot(player)
        };

        SetState(root, state);
        await SaveDocumentAsync(playerId, root, cancellationToken);

        return new WheelSpinResult
        {
            RewardId = rewardId,
            AvailableSpins = state.AvailableSpins,
            UpdatedAt = state.UpdatedAt,
            NextUpdateAt = state.NextUpdateAt,
            RewardGrant = rewardGrantResponse
        };
    }

    private async Task<JsonObject> LoadSaveDocumentAsync(string playerId, CancellationToken cancellationToken)
    {
        var save = await _saveRepository.LoadAsync(playerId, cancellationToken);
        if (save is null || string.IsNullOrWhiteSpace(save.Data))
        {
            return [];
        }

        try
        {
            var parsed = JsonNode.Parse(save.Data);
            return parsed as JsonObject ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveDocumentAsync(string playerId, JsonObject document, CancellationToken cancellationToken)
    {
        await _saveRepository.SaveAsync(playerId, document.ToJsonString(), cancellationToken);
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
        if (!root.TryGetPropertyValue(FortuneWheelPropertyName, out var fortuneWheelNode) ||
            fortuneWheelNode is not JsonObject fortuneWheel)
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics();
            return false;
        }

        var availableSpins = TryGetInt(fortuneWheel, AvailableSpinsPropertyName);
        var rawUpdatedAt = TryGetLong(fortuneWheel, UpdatedAtPropertyName);
        if (availableSpins is null || availableSpins < 0 || availableSpins > MaxAvailableSpins)
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics();
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
                TimestampWasRepaired = true
            };
            return true;
        }

        if (!TryNormalizeTimestamp(rawUpdatedAt.Value, now, out var normalizedTimestamp, out var timestampWasNormalized, out _))
        {
            state = default!;
            diagnostics = new WheelStateReadDiagnostics();
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
            TimestampWasNormalized = timestampWasNormalized
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

        if (rawTimestamp <= 9_999_999_999)
        {
            if (rawTimestamp > long.MaxValue / 1000)
            {
                failureReason = "UpdatedAtSecondsOverflow";
                return false;
            }

            normalizedTimestamp = rawTimestamp * 1000;
            timestampWasNormalized = true;
            return normalizedTimestamp <= now;
        }

        if (rawTimestamp <= now)
        {
            return true;
        }

        failureReason = "UpdatedAtMillisecondsInFuture";
        return false;
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
        public bool TimestampWasNormalized { get; init; }
        public bool TimestampWasRepaired { get; init; }
    }

    private sealed class NoOpRewardPartApplier : IRewardPartApplier
    {
        public Task ApplyAsync(Models.Domain.Rewards.PlayerAggregate player, Models.Domain.Rewards.RewardDefinition rewardDefinition, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyRewardDefinitionRepository : IRewardDefinitionRepository
    {
        public Task<Models.Domain.Rewards.RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<Models.Domain.Rewards.RewardDefinition?>(new Models.Domain.Rewards.RewardDefinition
            {
                RewardId = rewardId,
                Source = WheelRewardSource,
                Parts = []
            });
        }
    }
}
