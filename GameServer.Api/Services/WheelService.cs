using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class WheelService : IWheelService
{
    private const int MaxAvailableSpins = 8;
    private static readonly TimeSpan RegenInterval = TimeSpan.FromMinutes(15);
    private static readonly long RegenIntervalMs = (long)RegenInterval.TotalMilliseconds;
    private static readonly int DefaultNextRegenSeconds = (int)RegenInterval.TotalSeconds;

    private readonly ISaveRepository _saveRepository;
    private readonly IWheelRewardCatalog _rewardCatalog;
    private readonly ISystemClock _clock;

    public WheelService(ISaveRepository saveRepository, IWheelRewardCatalog rewardCatalog, ISystemClock clock)
    {
        _saveRepository = saveRepository;
        _rewardCatalog = rewardCatalog;
        _clock = clock;
    }

    public IReadOnlyList<WheelReward> GetRewards() => _rewardCatalog.GetRewards();

    public async Task<WheelStateResult> GetStateAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);

        var now = _clock.UtcNowUnixMilliseconds;
        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        if (!TryReadExistingState(root, now, out var state))
        {
            return new WheelStateResult
            {
                AvailableSpins = MaxAvailableSpins,
                NextRegenSeconds = DefaultNextRegenSeconds
            };
        }

        ApplyRegenIfNeeded(ref state, now);
        return new WheelStateResult
        {
            AvailableSpins = state.AvailableSpins,
            NextRegenSeconds = CalculateNextRegenSeconds(state, now)
        };
    }

    public async Task<WheelSpinResult> SpinAsync(string playerId, CancellationToken cancellationToken)
    {
        ValidatePlayerId(playerId);

        var now = _clock.UtcNowUnixMilliseconds;
        var root = await LoadSaveDocumentAsync(playerId, cancellationToken);
        var state = ReadState(root, now);

        ApplyRegenIfNeeded(ref state, now);
        if (state.AvailableSpins <= 0)
        {
            SetState(root, state);
            await SaveDocumentAsync(playerId, root, cancellationToken);
            throw new NoSpinsAvailableException(CalculateNextRegenSeconds(state, now));
        }

        var rewardId = _rewardCatalog.RollRewardId();
        state.AvailableSpins -= 1;

        SetState(root, state);
        await SaveDocumentAsync(playerId, root, cancellationToken);

        return new WheelSpinResult
        {
            RewardId = rewardId,
            AvailableSpins = state.AvailableSpins,
            NextRegenSeconds = CalculateNextRegenSeconds(state, now)
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
        var payload = document.ToJsonString();
        await _saveRepository.SaveAsync(playerId, payload, cancellationToken);
    }

    private static WheelState ReadState(JsonObject root, long now)
    {
        if (TryReadExistingState(root, now, out var state))
        {
            return state;
        }

        return new WheelState
        {
            AvailableSpins = MaxAvailableSpins,
            LastResetTimestamp = now
        };
    }

    private static bool TryReadExistingState(JsonObject root, long now, out WheelState state)
    {
        var availableSpins = TryGetInt(root, "availableSpins");
        var lastResetTimestamp = TryGetLong(root, "lastResetTimestamp");
        if (availableSpins is null ||
            availableSpins < 0 ||
            availableSpins > MaxAvailableSpins ||
            lastResetTimestamp is null ||
            lastResetTimestamp <= 0 ||
            lastResetTimestamp > now)
        {
            state = default;
            return false;
        }

        state = new WheelState
        {
            AvailableSpins = availableSpins.Value,
            LastResetTimestamp = lastResetTimestamp.Value
        };
        return true;
    }

    private static void ApplyRegenIfNeeded(ref WheelState state, long now)
    {
        if (now - state.LastResetTimestamp < RegenIntervalMs)
        {
            return;
        }

        state.AvailableSpins = MaxAvailableSpins;
        state.LastResetTimestamp = now;
    }

    private static int CalculateNextRegenSeconds(WheelState state, long now)
    {
        if (state.AvailableSpins >= MaxAvailableSpins)
        {
            return 0;
        }

        var elapsed = now - state.LastResetTimestamp;
        var remainingMs = RegenIntervalMs - elapsed;
        if (remainingMs <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(remainingMs / 1000d);
    }

    private static void SetState(JsonObject root, WheelState state)
    {
        root["availableSpins"] = state.AvailableSpins;
        root["lastResetTimestamp"] = state.LastResetTimestamp;
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

    private struct WheelState
    {
        public int AvailableSpins { get; set; }
        public long LastResetTimestamp { get; set; }
    }
}
