using GameServer.Api.Models.Dto.LiveOps;

namespace GameServer.Api.Services.Interfaces;

public interface ILiveOpsScheduleService
{
    Task<IReadOnlyList<LiveOpsScheduleItemDto>> GetScheduleAsync(CancellationToken ct = default);
}
