using GameServer.Api.Models.Dto.Admin;

namespace GameServer.Api.Services.Interfaces;

public interface ISaveStorageBackfillService
{
    Task<SaveStorageBackfillResponse> BackfillAsync(bool overwrite, CancellationToken ct = default);
    Task<SaveStorageStatusResponse> GetStatusAsync(CancellationToken ct = default);
}
