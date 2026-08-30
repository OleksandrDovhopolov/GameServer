using GameServer.Api.Models.Dto.Admin;

namespace GameServer.Api.Services.Interfaces;

public interface IAdminMaintenanceService
{
    Task<AdminMaintenanceOperationResponse> FlushRedisAsync(AdminRedisFlushRequest request, CancellationToken ct = default);
    Task<AdminMaintenanceOperationResponse> ResetPlayerSaveAsync(string playerId, AdminPlayerSaveResetRequest request, CancellationToken ct = default);
}

public interface IRedisAdminClient
{
    Task FlushDatabaseAsync(CancellationToken ct = default);
}
