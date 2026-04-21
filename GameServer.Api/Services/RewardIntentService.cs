using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Models.Dto.RewardIntents;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class RewardIntentService : IRewardIntentService
{
    private static readonly TimeSpan IntentTtl = TimeSpan.FromMinutes(30);

    private readonly IRewardIntentRepository _rewardIntentRepository;
    private readonly IPlayerEconomyRepository _playerEconomyRepository;
    private readonly IRewardDefinitionRepository _rewardDefinitionRepository;
    private readonly IRewardApplicationService _rewardApplicationService;
    private readonly ISystemClock _clock;
    private readonly ILogger<RewardIntentService> _logger;

    public RewardIntentService(
        IRewardIntentRepository rewardIntentRepository,
        IPlayerEconomyRepository playerEconomyRepository,
        IRewardDefinitionRepository rewardDefinitionRepository,
        IRewardApplicationService rewardApplicationService,
        ISystemClock clock,
        ILogger<RewardIntentService> logger)
    {
        _rewardIntentRepository = rewardIntentRepository;
        _playerEconomyRepository = playerEconomyRepository;
        _rewardDefinitionRepository = rewardDefinitionRepository;
        _rewardApplicationService = rewardApplicationService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateRewardIntentResponse> CreateIntentAsync(string playerId, string rewardId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(rewardId))
        {
            return BuildCreateFailure(RewardIntentErrorCodes.ValidationError, "playerId and rewardId are required.");
        }

        var rewardDefinition = await _rewardDefinitionRepository.GetByIdAsync(rewardId, ct);
        if (rewardDefinition is null)
        {
            return BuildCreateFailure(RewardIntentErrorCodes.RewardNotSupported, $"Reward '{rewardId}' is not supported.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(playerId, ct);
        if (!playerExists)
        {
            return BuildCreateFailure(RewardIntentErrorCodes.PlayerNotFound, "Player was not found.");
        }

        var createdAtUtc = _clock.UtcNowUnixMilliseconds;
        var expiresAtUtc = createdAtUtc + (long)IntentTtl.TotalMilliseconds;

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

        var lockToken = Guid.NewGuid().ToString("N");
        var beginResult = await _rewardIntentRepository.TryBeginCallbackProcessingAsync(
            provider,
            rewardIntentId,
            externalEventId,
            _clock.UtcNowUnixMilliseconds,
            lockToken,
            ct);

        if (!string.Equals(beginResult.ResultCode, "CLAIMED", StringComparison.Ordinal))
        {
            LogBeginFailure(provider, rewardIntentId, externalEventId, beginResult);
            return new ConfirmRewardIntentCallbackResponse
            {
                Success = false,
                Status = NormalizeStatus(beginResult.Status),
                ErrorCode = beginResult.ResultCode,
                ErrorMessage = beginResult.ErrorMessage
            };
        }

        if (string.IsNullOrWhiteSpace(beginResult.PlayerId) || string.IsNullOrWhiteSpace(beginResult.RewardId))
        {
            await _rewardIntentRepository.FinalizeCallbackProcessingAsync(
                provider,
                rewardIntentId,
                externalEventId,
                lockToken,
                RewardIntentStatuses.Failed,
                RewardIntentErrorCodes.RewardGrantFailed,
                "Claimed callback is missing playerId/rewardId.",
                _clock.UtcNowUnixMilliseconds,
                ct);

            return new ConfirmRewardIntentCallbackResponse
            {
                Success = false,
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.RewardGrantFailed,
                ErrorMessage = "Claimed callback is missing playerId/rewardId."
            };
        }

        var grantResponse = await _rewardApplicationService.GrantAsync(
            new GrantRewardCommand
            {
                PlayerId = beginResult.PlayerId,
                RewardId = beginResult.RewardId,
                RewardSource = "reward_intent_callback"
            },
            ct);

        var (finalStatus, finalErrorCode, finalErrorMessage) = MapGrantResult(grantResponse);

        var finalized = await _rewardIntentRepository.FinalizeCallbackProcessingAsync(
            provider,
            rewardIntentId,
            externalEventId,
            lockToken,
            finalStatus,
            finalErrorCode,
            finalErrorMessage,
            _clock.UtcNowUnixMilliseconds,
            ct);

        if (!finalized)
        {
            _logger.LogError(
                "Reward callback finalization failed. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}",
                rewardIntentId,
                provider,
                externalEventId);
            return new ConfirmRewardIntentCallbackResponse
            {
                Success = false,
                Status = RewardIntentStatuses.Failed,
                ErrorCode = RewardIntentErrorCodes.RewardGrantFailed,
                ErrorMessage = "Reward callback finalization failed."
            };
        }

        if (string.Equals(finalStatus, RewardIntentStatuses.Fulfilled, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Reward granted from callback through standard grant flow. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}, RewardId: {RewardId}",
                rewardIntentId,
                provider,
                externalEventId,
                beginResult.RewardId);
            return new ConfirmRewardIntentCallbackResponse
            {
                Success = true,
                Status = RewardIntentStatuses.Fulfilled,
                ErrorCode = null,
                ErrorMessage = null
            };
        }

        return new ConfirmRewardIntentCallbackResponse
        {
            Success = false,
            Status = finalStatus,
            ErrorCode = finalErrorCode,
            ErrorMessage = finalErrorMessage
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
            ErrorCode = string.Equals(status, RewardIntentStatuses.Pending, StringComparison.Ordinal) ? null : intent.ErrorCode,
            ErrorMessage = string.Equals(status, RewardIntentStatuses.Pending, StringComparison.Ordinal) ? null : intent.ErrorMessage
        };
    }

    private void LogBeginFailure(string provider, string rewardIntentId, string externalEventId, RewardIntentConfirmResult beginResult)
    {
        if (string.Equals(beginResult.ResultCode, RewardIntentErrorCodes.CallbackDuplicate, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Reward callback duplicate ignored. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}",
                rewardIntentId,
                provider,
                externalEventId);
            return;
        }

        if (string.Equals(beginResult.ResultCode, RewardIntentErrorCodes.CallbackInvalid, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Reward callback invalid. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}, Status: {Status}",
                rewardIntentId,
                provider,
                externalEventId,
                beginResult.Status);
            return;
        }

        _logger.LogError(
            "Reward callback rejected before grant execution. RewardIntentId: {RewardIntentId}, Provider: {Provider}, ExternalEventId: {ExternalEventId}, Code: {Code}",
            rewardIntentId,
            provider,
            externalEventId,
            beginResult.ResultCode);
    }

    private static (string FinalStatus, string ErrorCode, string ErrorMessage) MapGrantResult(GrantRewardResponse grantResponse)
    {
        if (grantResponse.Success)
        {
            return (RewardIntentStatuses.Fulfilled, string.Empty, string.Empty);
        }

        return grantResponse.ErrorCode switch
        {
            RewardErrorCodes.PlayerNotFound => (
                RewardIntentStatuses.Rejected,
                RewardIntentErrorCodes.PlayerNotFound,
                grantResponse.ErrorMessage ?? "Player was not found."),
            RewardErrorCodes.RewardNotFound => (
                RewardIntentStatuses.Rejected,
                RewardIntentErrorCodes.RewardNotSupported,
                grantResponse.ErrorMessage ?? "Reward was not found."),
            RewardErrorCodes.UnsupportedRewardKind => (
                RewardIntentStatuses.Failed,
                RewardIntentErrorCodes.RewardGrantFailed,
                grantResponse.ErrorMessage ?? "Unsupported reward kind."),
            RewardErrorCodes.RewardApplicationFailed => (
                RewardIntentStatuses.Failed,
                RewardIntentErrorCodes.RewardGrantFailed,
                grantResponse.ErrorMessage ?? "Reward grant failed."),
            _ => (
                RewardIntentStatuses.Failed,
                RewardIntentErrorCodes.RewardGrantFailed,
                grantResponse.ErrorMessage ?? "Reward grant failed.")
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
