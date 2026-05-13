using GameServer.Api.Models.Dto.Rewards;

namespace GameServer.Api.Services.Interfaces;

public interface IRewardApplicationService
{
    Task<GrantRewardResponse> GrantAsync(GrantRewardCommand command, CancellationToken ct = default);
}
