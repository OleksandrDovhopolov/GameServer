using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class BattlePassLiveOpsProvider : ILiveOpsScheduleItemProvider
{
    private const int BattlePassPriority = 1;

    private readonly IBattlePassSeasonRepository _seasonRepository;
    private readonly ISystemClock _clock;

    public BattlePassLiveOpsProvider(IBattlePassSeasonRepository seasonRepository, ISystemClock clock)
    {
        _seasonRepository = seasonRepository;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LiveOpsScheduleItem>> GetItemsAsync(CancellationToken ct = default)
    {
        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var seasons = await _seasonRepository.GetAllAsync(ct);
        return seasons
            .Select(x => BattlePassSeasonState.Normalize(x, nowUtcMs))
            .Where(x =>
                string.Equals(x.Status, BattlePassSeasonStatuses.Scheduled, StringComparison.Ordinal) ||
                string.Equals(x.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
            .OrderBy(x => x.StartAtUtcMs)
            .Select(x => new LiveOpsScheduleItem
            {
                Id = x.Id,
                EventType = "BattlePass",
                StreamId = "battle_pass",
                StartTimeUtcMs = x.StartAtUtcMs,
                EndTimeUtcMs = x.EndAtUtcMs,
                Priority = BattlePassPriority,
                CustomParams = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["seasonId"] = x.Id,
                    ["title"] = x.Title,
                    ["configVersion"] = x.ConfigVersion
                }
            })
            .ToList();
    }
}
