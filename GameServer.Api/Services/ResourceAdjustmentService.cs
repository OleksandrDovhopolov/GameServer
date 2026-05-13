using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Resources;
using GameServer.Api.Models.Dto.Resources;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class ResourceAdjustmentService : IResourceAdjustmentService
{
    private static readonly HashSet<string> SupportedResourceIds = new(StringComparer.Ordinal)
    {
        "Gold",
        "Energy",
        "Gems"
    };

    private readonly ISaveRepository _saveRepository;
    private readonly IPlayerResourcesRepository _playerResourcesRepository;
    private readonly ILogger<ResourceAdjustmentService> _logger;

    public ResourceAdjustmentService(
        ISaveRepository saveRepository,
        IPlayerResourcesRepository playerResourcesRepository,
        ILogger<ResourceAdjustmentService> logger)
    {
        _saveRepository = saveRepository;
        _playerResourcesRepository = playerResourcesRepository;
        _logger = logger;
    }

    public async Task<AdjustResourceResult> AdjustAsync(AdjustResourceCommand command, CancellationToken ct = default)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            _logger.LogWarning(
                "Resource adjust validation failed. PlayerId: {PlayerId}, ResourceId: {ResourceId}, Delta: {Delta}, Reason: {Reason}, Error: {Error}",
                command?.PlayerId,
                command?.ResourceId,
                command?.Delta,
                command?.Reason,
                validationError);
            return BuildFailure(ResourceAdjustmentErrorCodes.InvalidRequest, validationError);
        }

        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["PlayerId"] = command.PlayerId,
                   ["ResourceId"] = command.ResourceId,
                   ["Delta"] = command.Delta,
                   ["Reason"] = command.Reason
               }))
        {
            try
            {
                if (!SupportedResourceIds.Contains(command.ResourceId))
                {
                    _logger.LogWarning("Resource adjust failed: unsupported resource id.");
                    return BuildFailure(
                        ResourceAdjustmentErrorCodes.ResourceNotSupported,
                        $"Resource '{command.ResourceId}' is not supported.");
                }

                var playerExists = await _saveRepository.ExistsAsync(command.PlayerId, ct);
                if (!playerExists)
                {
                    _logger.LogWarning("Resource adjust failed: player not found.");
                    return BuildFailure(
                        ResourceAdjustmentErrorCodes.PlayerNotFound,
                        $"Player '{command.PlayerId}' was not found.");
                }

                var record = await _playerResourcesRepository.GetAsync(command.PlayerId, ct)
                             ?? CreateDefaultRecord(command.PlayerId);
                EnsureSupportedKeys(record);

                record.Resources.TryGetValue(command.ResourceId, out var currentValue);
                var nextValue = currentValue + command.Delta;
                if (command.Delta < 0 && (currentValue < -command.Delta || nextValue < 0))
                {
                    _logger.LogWarning(
                        "Resource adjust rejected due to insufficient resources. CurrentValue: {CurrentValue}, RequestedDelta: {RequestedDelta}",
                        currentValue,
                        command.Delta);
                    return BuildFailure(
                        ResourceAdjustmentErrorCodes.InsufficientResources,
                        $"Insufficient '{command.ResourceId}'.");
                }

                record.Resources[command.ResourceId] = nextValue;
                record.Version = checked(record.Version + 1);
                await _playerResourcesRepository.SaveAsync(record, ct);

                _logger.LogInformation("Resource adjust succeeded. NewValue: {NewValue}, Version: {Version}", nextValue, record.Version);

                return new AdjustResourceResult
                {
                    Success = true,
                    Snapshot = ToSnapshot(record)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resource adjust failed unexpectedly.");
                return BuildFailure(ResourceAdjustmentErrorCodes.ResourceAdjustmentFailed, "Resource adjustment failed.");
            }
        }
    }

    private static string? Validate(AdjustResourceCommand? command)
    {
        if (command is null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(command.PlayerId))
        {
            return "PlayerId is required.";
        }

        if (string.IsNullOrWhiteSpace(command.ResourceId))
        {
            return "ResourceId is required.";
        }

        if (command.Delta == 0)
        {
            return "Delta must not be zero.";
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return "Reason is required.";
        }

        return null;
    }

    private static PlayerResourcesRecord CreateDefaultRecord(string playerId)
    {
        return new PlayerResourcesRecord
        {
            PlayerId = playerId,
            Version = 0,
            Resources = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Gold"] = 0,
                ["Energy"] = 0,
                ["Gems"] = 0
            }
        };
    }

    private static void EnsureSupportedKeys(PlayerResourcesRecord record)
    {
        foreach (var resourceId in SupportedResourceIds)
        {
            if (!record.Resources.ContainsKey(resourceId))
            {
                record.Resources[resourceId] = 0;
            }
        }
    }

    private static ResourceSnapshotDto ToSnapshot(PlayerResourcesRecord record)
    {
        record.Resources.TryGetValue("Gold", out var gold);
        record.Resources.TryGetValue("Energy", out var energy);
        record.Resources.TryGetValue("Gems", out var gems);

        return new ResourceSnapshotDto
        {
            Gold = Math.Max(0, gold),
            Energy = Math.Max(0, energy),
            Gems = Math.Max(0, gems)
        };
    }

    private static AdjustResourceResult BuildFailure(string errorCode, string errorMessage)
    {
        return new AdjustResourceResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
