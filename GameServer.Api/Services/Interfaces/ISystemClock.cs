namespace GameServer.Api.Services.Interfaces;

public interface ISystemClock
{
    long UtcNowUnixMilliseconds { get; }
}
