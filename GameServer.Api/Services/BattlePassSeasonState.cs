using GameServer.Api.Models.Domain.BattlePass;

namespace GameServer.Api.Services;

public static class BattlePassSeasonState
{
    public static string GetEffectiveStatus(BattlePassSeasonRecord season, long nowUtcMs)
    {
        if (string.Equals(season.Status, BattlePassSeasonStatuses.Archived, StringComparison.Ordinal))
        {
            return BattlePassSeasonStatuses.Archived;
        }

        if (string.Equals(season.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal))
        {
            return BattlePassSeasonStatuses.Draft;
        }

        if (nowUtcMs >= season.EndAtUtcMs)
        {
            return BattlePassSeasonStatuses.Ended;
        }

        if (nowUtcMs >= season.StartAtUtcMs)
        {
            return BattlePassSeasonStatuses.Active;
        }

        return BattlePassSeasonStatuses.Scheduled;
    }

    public static BattlePassSeasonRecord Normalize(BattlePassSeasonRecord season, long nowUtcMs)
    {
        var effectiveStatus = GetEffectiveStatus(season, nowUtcMs);
        if (string.Equals(effectiveStatus, season.Status, StringComparison.Ordinal))
        {
            return season;
        }

        return new BattlePassSeasonRecord
        {
            Id = season.Id,
            Title = season.Title,
            StartAtUtcMs = season.StartAtUtcMs,
            EndAtUtcMs = season.EndAtUtcMs,
            MaxLevel = season.MaxLevel,
            Status = effectiveStatus,
            ConfigVersion = season.ConfigVersion,
            PremiumProductId = season.PremiumProductId,
            PlatinumProductId = season.PlatinumProductId,
            CreatedAtUtcMs = season.CreatedAtUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        };
    }

    public static bool IsPurchasable(string status)
    {
        return string.Equals(status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal);
    }
}
