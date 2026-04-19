using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class RewardApplicationService : IRewardApplicationService
{
    private readonly ISaveRepository _saveRepository;
    private readonly IPlayerAggregateRepository _playerAggregateRepository;
    private readonly IRewardDefinitionRepository _rewardDefinitionRepository;
    private readonly IRewardPartApplier _rewardPartApplier;
    private readonly ILogger<RewardApplicationService> _logger;

    public RewardApplicationService(
        ISaveRepository saveRepository,
        IPlayerAggregateRepository playerAggregateRepository,
        IRewardDefinitionRepository rewardDefinitionRepository,
        IRewardPartApplier rewardPartApplier,
        ILogger<RewardApplicationService> logger)
    {
        _saveRepository = saveRepository;
        _playerAggregateRepository = playerAggregateRepository;
        _rewardDefinitionRepository = rewardDefinitionRepository;
        _rewardPartApplier = rewardPartApplier;
        _logger = logger;
    }

    public async Task<GrantRewardResponse> GrantAsync(GrantRewardCommand command, CancellationToken ct = default)
    {
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            _logger.LogWarning("Reward grant validation failed: {ValidationError}", validationError);
            return BuildFailure(command, RewardErrorCodes.InvalidRequest, validationError);
        }

        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["RewardId"] = command.RewardId
               }))
        {
            try
            {
                var playerExists = await _saveRepository.ExistsAsync(command.PlayerId, ct);
                if (!playerExists)
                {
                    _logger.LogWarning("Reward grant failed: player not found.");
                    return BuildFailure(command, RewardErrorCodes.PlayerNotFound, $"Player '{command.PlayerId}' was not found.");
                }

                var rewardDefinition = await _rewardDefinitionRepository.GetByIdAsync(command.RewardId, ct);
                if (rewardDefinition is null)
                {
                    _logger.LogWarning("Reward grant failed: reward definition not found.");
                    return BuildFailure(command, RewardErrorCodes.RewardNotFound, $"Reward '{command.RewardId}' was not found.");
                }

                var player = await _playerAggregateRepository.LoadAsync(command.PlayerId, ct);
                await _rewardPartApplier.ApplyAsync(player, rewardDefinition, ct);
                var successfulResponse = new GrantRewardResponse
                {
                    Success = true,
                    RewardId = command.RewardId,
                    PlayerState = PlayerAggregateDocument.ToSnapshot(player)
                };
                await _playerAggregateRepository.SaveAsync(player, ct);

                return successfulResponse;
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException invalidOperationException &&
                    invalidOperationException.Message.StartsWith($"{RewardErrorCodes.UnsupportedRewardKind}:", StringComparison.Ordinal))
                {
                    _logger.LogError(ex, "Reward grant failed: unsupported reward kind.");
                    return BuildFailure(command, RewardErrorCodes.UnsupportedRewardKind, "Reward definition contains unsupported reward kind.");
                }

                _logger.LogError(ex, "Reward grant failed unexpectedly.");
                return BuildFailure(command, RewardErrorCodes.RewardApplicationFailed, "Reward application failed.");
            }
        }
    }

    private static string? ValidateCommand(GrantRewardCommand? command)
    {
        if (command is null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(command.PlayerId))
        {
            return "PlayerId is required.";
        }

        if (string.IsNullOrWhiteSpace(command.RewardId))
        {
            return "RewardId is required.";
        }

        return null;
    }

    private static GrantRewardResponse BuildFailure(GrantRewardCommand? command, string errorCode, string errorMessage)
    {
        return new GrantRewardResponse
        {
            Success = false,
            RewardId = command?.RewardId ?? string.Empty,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
