using GameServer.Api.Models.Domain.Rewards;

namespace GameServer.Api.Services.Interfaces;

public interface IGameSaveAssembler
{
    string BuildSaveJson(PlayerAggregate player);
}
