using GameServer.Api.Models.Dto.LiveOps;

namespace GameServer.Api.Services.Interfaces;

public interface ILiveOpsScheduleService
{
    Task<PublicLiveOpsScheduleResponseDto> GetScheduleAsync(CancellationToken ct = default);
}
