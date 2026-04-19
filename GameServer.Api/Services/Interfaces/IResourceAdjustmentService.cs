using GameServer.Api.Models.Domain.Resources;
using GameServer.Api.Models.Dto.Resources;

namespace GameServer.Api.Services.Interfaces;

public interface IResourceAdjustmentService
{
    Task<AdjustResourceResult> AdjustAsync(AdjustResourceCommand command, CancellationToken ct = default);
}
