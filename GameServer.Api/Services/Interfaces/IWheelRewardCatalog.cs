using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services.Interfaces;

public interface IWheelRewardCatalog
{
    IReadOnlyList<WheelReward> GetRewards();
    string RollRewardId();
}
