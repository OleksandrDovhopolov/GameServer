using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class SystemClock : ISystemClock
{
    public long UtcNowUnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
