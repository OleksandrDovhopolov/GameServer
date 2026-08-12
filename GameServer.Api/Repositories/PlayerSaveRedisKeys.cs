using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public static class PlayerSaveRedisKeys
{
    public const string Prefix = "save:";
    public const string ScanPattern = "save:*";

    public static RedisKey Save(string playerId) => $"save:{playerId}:{playerId}";
    public static RedisKey Meta(string playerId) => $"save:{playerId}:meta";
    public static RedisKey Resources(string playerId) => $"save:{playerId}:resources";
    public static RedisKey ResourcesVersion(string playerId) => $"save:{playerId}:resources:version";
    public static RedisKey Inventory(string playerId) => $"save:{playerId}:inventory";
    public static RedisKey InventoryVersion(string playerId) => $"save:{playerId}:inventory:version";

    public static bool TryGetPlayerId(string key, out string playerId)
    {
        playerId = string.Empty;
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var start = Prefix.Length;
        var nextSeparator = key.IndexOf(':', start);
        if (nextSeparator <= start)
        {
            return false;
        }

        playerId = key[start..nextSeparator];
        return !string.IsNullOrWhiteSpace(playerId);
    }
}
