using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class RandomGenerator : IRandomGenerator
{
    public int Next(int maxValue) => Random.Shared.Next(maxValue);
}
