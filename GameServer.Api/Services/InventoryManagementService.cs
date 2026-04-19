using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Models.Dto.Inventory;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class InventoryManagementService : IInventoryManagementService
{
    private readonly ISaveRepository _saveRepository;
    private readonly IPlayerInventoryRepository _playerInventoryRepository;
    private readonly IPlayerAggregateRepository _playerAggregateRepository;
    private readonly ILogger<InventoryManagementService> _logger;

    public InventoryManagementService(
        ISaveRepository saveRepository,
        IPlayerInventoryRepository playerInventoryRepository,
        IPlayerAggregateRepository playerAggregateRepository,
        ILogger<InventoryManagementService> logger)
    {
        _saveRepository = saveRepository;
        _playerInventoryRepository = playerInventoryRepository;
        _playerAggregateRepository = playerAggregateRepository;
        _logger = logger;
    }

    public Task<InventoryOperationResult> AddAsync(AddInventoryItemCommand command, CancellationToken ct = default)
    {
        return ExecuteAsync(
            command,
            ValidateAddCommand,
            command.PlayerId,
            command.Reason,
            command.ItemId,
            command.Amount,
            async (record, token) =>
            {
                record.Items.TryGetValue(command.ItemId, out var currentValue);
                record.Items[command.ItemId] = checked(currentValue + command.Amount);
                await SaveRecordAsync(record, token);
                return await BuildSuccessAsync(command.PlayerId, record, token);
            },
            ct);
    }

    public Task<InventoryOperationResult> RemoveAsync(RemoveInventoryItemCommand command, CancellationToken ct = default)
    {
        return ExecuteAsync(
            command,
            ValidateRemoveCommand,
            command.PlayerId,
            command.Reason,
            command.ItemId,
            command.Amount,
            async (record, token) =>
            {
                record.Items.TryGetValue(command.ItemId, out var currentValue);
                if (currentValue < command.Amount)
                {
                    _logger.LogWarning(
                        "Inventory remove rejected due to insufficient amount. CurrentValue: {CurrentValue}, RequestedAmount: {RequestedAmount}",
                        currentValue,
                        command.Amount);
                    return BuildFailure(
                        InventoryErrorCodes.InsufficientInventory,
                        $"Insufficient '{command.ItemId}'.");
                }

                var nextValue = currentValue - command.Amount;
                if (nextValue == 0)
                {
                    record.Items.Remove(command.ItemId);
                }
                else
                {
                    record.Items[command.ItemId] = nextValue;
                }

                await SaveRecordAsync(record, token);
                return await BuildSuccessAsync(command.PlayerId, record, token);
            },
            ct);
    }

    public Task<InventoryOperationResult> RemoveBatchAsync(RemoveInventoryBatchCommand command, CancellationToken ct = default)
    {
        return ExecuteAsync(
            command,
            ValidateRemoveBatchCommand,
            command.PlayerId,
            command.Reason,
            itemId: null,
            amount: null,
            async (record, token) =>
            {
                var aggregated = AggregateBatch(command.Items);

                foreach (var (itemId, requestedAmount) in aggregated)
                {
                    record.Items.TryGetValue(itemId, out var currentValue);
                    if (currentValue < requestedAmount)
                    {
                        _logger.LogWarning(
                            "Inventory batch remove rejected due to insufficient amount. ItemId: {ItemId}, CurrentValue: {CurrentValue}, RequestedAmount: {RequestedAmount}",
                            itemId,
                            currentValue,
                            requestedAmount);
                        return BuildFailure(
                            InventoryErrorCodes.InsufficientInventory,
                            $"Insufficient '{itemId}'.");
                    }
                }

                foreach (var (itemId, requestedAmount) in aggregated)
                {
                    var nextValue = record.Items[itemId] - requestedAmount;
                    if (nextValue == 0)
                    {
                        record.Items.Remove(itemId);
                    }
                    else
                    {
                        record.Items[itemId] = nextValue;
                    }
                }

                await SaveRecordAsync(record, token);
                return await BuildSuccessAsync(command.PlayerId, record, token);
            },
            ct);
    }

    private async Task<InventoryOperationResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, string?> validate,
        string playerId,
        string reason,
        string? itemId,
        int? amount,
        Func<PlayerInventoryRecord, CancellationToken, Task<InventoryOperationResult>> operation,
        CancellationToken ct)
    {
        var validationError = validate(command);
        if (validationError is not null)
        {
            _logger.LogWarning(
                "Inventory operation validation failed. PlayerId: {PlayerId}, ItemId: {ItemId}, Amount: {Amount}, Reason: {Reason}, Error: {Error}",
                playerId,
                itemId,
                amount,
                reason,
                validationError);
            return BuildFailure(InventoryErrorCodes.InvalidRequest, validationError);
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["PlayerId"] = playerId,
                   ["ItemId"] = itemId,
                   ["Amount"] = amount,
                   ["Reason"] = reason
               }))
        {
            try
            {
                var playerExists = await _saveRepository.ExistsAsync(playerId, ct);
                if (!playerExists)
                {
                    _logger.LogWarning("Inventory operation failed: player not found.");
                    return BuildFailure(
                        InventoryErrorCodes.PlayerNotFound,
                        $"Player '{playerId}' was not found.");
                }

                var record = await _playerInventoryRepository.GetAsync(playerId, ct)
                             ?? CreateDefaultRecord(playerId);
                EnsureNonNegative(record);

                return await operation(record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inventory operation failed unexpectedly.");
                return BuildFailure(
                    InventoryErrorCodes.InventoryAdjustmentFailed,
                    "Inventory adjustment failed.");
            }
        }
    }

    private static string? ValidateAddCommand(AddInventoryItemCommand? command)
    {
        if (command is null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(command.PlayerId))
        {
            return "PlayerId is required.";
        }

        if (string.IsNullOrWhiteSpace(command.ItemId))
        {
            return "ItemId is required.";
        }

        if (command.Amount <= 0)
        {
            return "Amount must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return "Reason is required.";
        }

        return null;
    }

    private static string? ValidateRemoveCommand(RemoveInventoryItemCommand? command)
    {
        if (command is null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(command.PlayerId))
        {
            return "PlayerId is required.";
        }

        if (string.IsNullOrWhiteSpace(command.ItemId))
        {
            return "ItemId is required.";
        }

        if (command.Amount <= 0)
        {
            return "Amount must be greater than zero.";
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return "Reason is required.";
        }

        return null;
    }

    private static string? ValidateRemoveBatchCommand(RemoveInventoryBatchCommand? command)
    {
        if (command is null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(command.PlayerId))
        {
            return "PlayerId is required.";
        }

        if (command.Items.Count == 0)
        {
            return "Items are required.";
        }

        for (var i = 0; i < command.Items.Count; i++)
        {
            var item = command.Items[i];
            if (string.IsNullOrWhiteSpace(item.ItemId))
            {
                return $"Items[{i}].ItemId is required.";
            }

            if (item.Amount <= 0)
            {
                return $"Items[{i}].Amount must be greater than zero.";
            }
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return "Reason is required.";
        }

        return null;
    }

    private static Dictionary<string, int> AggregateBatch(IEnumerable<InventoryItemDelta> items)
    {
        var aggregated = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            aggregated.TryGetValue(item.ItemId, out var currentValue);
            aggregated[item.ItemId] = checked(currentValue + item.Amount);
        }

        return aggregated;
    }

    private static void EnsureNonNegative(PlayerInventoryRecord record)
    {
        foreach (var (itemId, amount) in record.Items.ToList())
        {
            if (amount < 0)
            {
                throw new InvalidOperationException($"Inventory contains negative amount for '{itemId}'.");
            }

            if (amount == 0)
            {
                record.Items.Remove(itemId);
            }
        }
    }

    private async Task SaveRecordAsync(PlayerInventoryRecord record, CancellationToken ct)
    {
        record.Version = checked(record.Version + 1);
        await _playerInventoryRepository.SaveAsync(record, ct);
    }

    private async Task<InventoryOperationResult> BuildSuccessAsync(
        string playerId,
        PlayerInventoryRecord record,
        CancellationToken ct)
    {
        var aggregate = await _playerAggregateRepository.LoadAsync(playerId, ct);
        return new InventoryOperationResult
        {
            Success = true,
            Snapshot = ToSnapshot(record),
            PlayerState = PlayerAggregateDocument.ToSnapshot(aggregate)
        };
    }

    private static PlayerInventoryRecord CreateDefaultRecord(string playerId)
    {
        return new PlayerInventoryRecord
        {
            PlayerId = playerId,
            Version = 0,
            Items = new Dictionary<string, int>(StringComparer.Ordinal)
        };
    }

    private static InventorySnapshotDto ToSnapshot(PlayerInventoryRecord record)
    {
        return new InventorySnapshotDto
        {
            Items = record.Items
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
        };
    }

    private static InventoryOperationResult BuildFailure(string errorCode, string errorMessage)
    {
        return new InventoryOperationResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
