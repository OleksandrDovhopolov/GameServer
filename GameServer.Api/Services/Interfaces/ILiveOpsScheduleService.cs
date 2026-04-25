using GameServer.Api.Models.Dto.LiveOps;

namespace GameServer.Api.Services.Interfaces;

public interface ILiveOpsScheduleService
{
    Task<IReadOnlyList<PublicLiveOpsScheduleItemDto>> GetScheduleAsync(CancellationToken ct = default);
}
