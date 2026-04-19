using GameServer.Api.Models.Domain.Inventory;
using GameServer.Api.Models.Dto.Inventory;

namespace GameServer.Api.Services.Interfaces;

public interface IInventoryManagementService
{
    Task<InventoryOperationResult> AddAsync(AddInventoryItemCommand command, CancellationToken ct = default);
    Task<InventoryOperationResult> RemoveAsync(RemoveInventoryItemCommand command, CancellationToken ct = default);
    Task<InventoryOperationResult> RemoveBatchAsync(RemoveInventoryBatchCommand command, CancellationToken ct = default);
}
