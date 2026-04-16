using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface IWheelService
{
    IReadOnlyList<WheelReward> GetRewards();
    Task<WheelSpinResult> SpinAsync(string playerId, CancellationToken cancellationToken);
}
