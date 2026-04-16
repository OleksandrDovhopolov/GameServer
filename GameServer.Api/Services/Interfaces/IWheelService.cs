using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface IWheelService
{
    IReadOnlyList<WheelReward> GetRewards();
    Task<WheelStateResult> GetStateAsync(string playerId, CancellationToken cancellationToken);
    Task<WheelSpinResult> SpinAsync(string playerId, CancellationToken cancellationToken);
}
