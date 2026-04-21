using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Models.Dto.RewardIntents;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class RewardIntentService : IRewardIntentService
{
    private readonly IRewardIntentRepository _rewardIntentRepository;
    private readonly IPlayerEconomyRepository _playerEconomyRepository;
    private readonly ISystemClock _clock;
    private readonly ILogger<RewardIntentService> _logger;

    public RewardIntentService(
        IRewardIntentRepository rewardIntentRepository,
        IPlayerEconomyRepository playerEconomyRepository,
        ISystemClock clock,
        ILogger<RewardIntentService> logger)
    {
        _rewardIntentRepository = rewardIntentRepository;
        _playerEconomyRepository = playerEconomyRepository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateRewardIntentResponse> CreateIntentAsync(string playerId, string rewardId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(rewardId))
        {
            return BuildCreateFailure(RewardIntentErrorCodes.ValidationError, "playerId and rewardId are required.");
        }

        if (!string.Equals(rewardId, RewardIntentDefaults.SupportedRewardId, StringComparison.Ordinal))
        {
            return BuildCreateFailure(RewardIntentErrorCodes.RewardNotSupported, $"Reward '{rewardId}' is not supported.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(playerId, ct);
        if (!playerExists)
        {
            return BuildCreateFailure(RewardIntentErrorCodes.PlayerNotFound, "Player was not found.");
        }

        var createdAtUtc = _clock.UtcNowUnixMilliseconds;
        var expiresAtUtc = createdAtUtc + (long)RewardIntentDefaults.IntentTtl.TotalMilliseconds;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var rewardIntentId = $"ri_{Guid.NewGuid():N}";
            var intent = new RewardIntentRecord
            {
                RewardIntentId = rewardIntentId,
                PlayerId = playerId,
                RewardId = rewardId,
                Status = RewardIntentStatuses.Pending,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
                ExpiresAtUtc = expiresAtUtc
            };

            var created = await _rewardIntentRepository.CreateAsync(intent, ct);
            if (!created)
            {
                _logger.LogWarning(
                    "Reward intent create collision. RewardIntentId: {RewardIntentId}, PlayerId: {PlayerId}",
                    rewardIntentId,
                    playerId);
                continue;
            }

            _logger.LogInformation(
                "Reward intent created. RewardIntentId: {RewardIntentId}, PlayerId: {PlayerId}, RewardId: {RewardId}, ExpiresAtUtc: {ExpiresAtUtc}",
                rewardIntentId,
                playerId,
                rewardId,
                expiresAtUtc);

            return new CreateRewardIntentResponse
            {
                Success = true,
                RewardIntentId = rewardIntentId
            };
        }

        return BuildCreateFailure(RewardIntentErrorCodes.IntentCreateFailed, "Reward intent creation failed.");
    }

    public async Task<GetRewardIntentStatusResponse> GetIntentStatusAsync(string rewardIntentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rewardIntentId))
        {
            return new GetRewardIntentStatusResponse
            {
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.ValidationError,
                ErrorMessage = "rewardIntentId is required."
            };
        }

        var intent = await _rewardIntentRepository.GetByIdAsync(rewardIntentId, ct);
        if (intent is null)
        {
            return new GetRewardIntentStatusResponse
            {
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.IntentNotFound,
                ErrorMessage = "Reward intent was not found."
            };
        }

        if (string.Equals(intent.Status, RewardIntentStatuses.Pending, StringComparison.Ordinal))
        {
            var nowUtc = _clock.UtcNowUnixMilliseconds;
            if (intent.ExpiresAtUtc <= nowUtc)
            {
                var expired = await _rewardIntentRepository.ExpireIfPendingAsync(
                    rewardIntentId,
                    nowUtc,
                    RewardIntentErrorCodes.IntentExpired,
                    "Reward intent expired.",
                    ct);
                if (expired)
                {
                    _logger.LogInformation(
                        "Reward intent expired by lazy status check. RewardIntentId: {RewardIntentId}",
                        rewardIntentId);
                    intent = await _rewardIntentRepository.GetByIdAsync(rewardIntentId, ct) ?? intent;
                }
            }
        }

        _logger.LogInformation(
            "Reward intent status requested. RewardIntentId: {RewardIntentId}, Status: {Status}",
            rewardIntentId,
            intent.Status);

        return ToStatusResponse(intent);
    }

    public async Task<ConfirmRewardIntentCallbackResponse> ConfirmIntentFromAdCallbackAsync(
        string provider,
        string rewardIntentId,
        string externalEventId,
        string? callbackPayload,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) ||
            string.IsNullOrWhiteSpace(rewardIntentId) ||
            string.IsNullOrWhiteSpace(externalEventId))
        {
            return new ConfirmRewardIntentCallbackResponse
            {
                Success = false,
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.ValidationError,
                ErrorMessage = "provider, rewardIntentId and externalEventId are required."
            };
        }

        _logger.LogInformation(
            "Reward callback received. Provider: {Provider}, RewardIntentId: {RewardIntentId}, ExternalEventId: {ExternalEventId}, PayloadLength: {PayloadLength}",
            provider,
            rewardIntentId,
            externalEventId,
            callbackPayload?.Length ?? 0);

        var result = await _rewardIntentRepository.ConfirmFromCallbackAsync(
            provider,
            rewardIntentId,
            externalEventId,
            RewardIntentDefaults.SupportedRewardId,
            RewardIntentDefaults.GemsAmount,
            _clock.UtcNowUnixMilliseconds,
            ct);

        if (string.Equals(result.ResultCode, "OK", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Reward granted from callback. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}, NewCrystalsBalance: {NewCrystalsBalance}",
                rewardIntentId,
                provider,
                externalEventId,
                result.NewCrystalsBalance);
            return new ConfirmRewardIntentCallbackResponse
            {
                Success = true,
                Status = RewardIntentStatuses.Fulfilled,
                NewCrystalsBalance = result.NewCrystalsBalance
            };
        }

        if (string.Equals(result.ResultCode, RewardIntentErrorCodes.CallbackDuplicate, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Reward callback duplicate ignored. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}",
                rewardIntentId,
                provider,
                externalEventId);
        }
        else if (string.Equals(result.ResultCode, RewardIntentErrorCodes.CallbackInvalid, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Reward callback invalid. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}, Status: {Status}",
                rewardIntentId,
                provider,
                externalEventId,
                result.Status);
        }
        else if (string.Equals(result.ResultCode, RewardIntentErrorCodes.RewardGrantFailed, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Reward grant failed during callback processing. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}",
                rewardIntentId,
                provider,
                externalEventId);
        }

        return new ConfirmRewardIntentCallbackResponse
        {
            Success = false,
            Status = NormalizeStatus(result.Status),
            NewCrystalsBalance = string.Equals(result.Status, RewardIntentStatuses.Fulfilled, StringComparison.Ordinal)
                ? result.NewCrystalsBalance
                : null,
            ErrorCode = result.ResultCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    private static CreateRewardIntentResponse BuildCreateFailure(string errorCode, string errorMessage)
    {
        return new CreateRewardIntentResponse
        {
            Success = false,
            RewardIntentId = null,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }

    private static GetRewardIntentStatusResponse ToStatusResponse(RewardIntentRecord intent)
    {
        var status = NormalizeStatus(intent.Status);
        return new GetRewardIntentStatusResponse
        {
            Status = status,
            NewCrystalsBalance = string.Equals(status, RewardIntentStatuses.Fulfilled, StringComparison.Ordinal)
                ? intent.NewCrystalsBalance
                : null,
            ErrorCode = string.Equals(status, RewardIntentStatuses.Pending, StringComparison.Ordinal) ? null : intent.ErrorCode,
            ErrorMessage = string.Equals(status, RewardIntentStatuses.Pending, StringComparison.Ordinal) ? null : intent.ErrorMessage
        };
    }

    private static string NormalizeStatus(string status)
    {
        return status switch
        {
            RewardIntentStatuses.Pending => RewardIntentStatuses.Pending,
            RewardIntentStatuses.Fulfilled => RewardIntentStatuses.Fulfilled,
            RewardIntentStatuses.Rejected => RewardIntentStatuses.Rejected,
            RewardIntentStatuses.Expired => RewardIntentStatuses.Expired,
            _ => RewardIntentStatuses.Failed
        };
    }
}
