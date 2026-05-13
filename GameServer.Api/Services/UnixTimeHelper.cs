namespace GameServer.Api.Services;

public static class UnixTimeHelper
{
    public static string ToUtcIsoString(long utcMs)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(utcMs).UtcDateTime.ToString("O");
    }
}
