namespace GameServer.Api.Models.Dto.BattlePass;

public sealed class BattlePassRewardDto
{
    public string RewardId { get; init; } = string.Empty;
}

public sealed class BattlePassClaimedRewardDto
{
    public int Level { get; init; }
    public string RewardTrack { get; init; } = string.Empty;
    public string ClaimedAtUtc { get; init; } = string.Empty;
}

public sealed class BattlePassClaimableRewardDto
{
    public int Level { get; init; }
    public string RewardTrack { get; init; } = string.Empty;
    public List<BattlePassRewardDto> Rewards { get; init; } = [];
}

public sealed class BattlePassLevelDto
{
    public int Level { get; init; }
    public int XpRequired { get; init; }
    public List<BattlePassRewardDto> DefaultRewards { get; init; } = [];
    public List<BattlePassRewardDto> PremiumRewards { get; init; } = [];
}

public sealed class BattlePassSeasonDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string StartAtUtc { get; init; } = string.Empty;
    public string EndAtUtc { get; init; } = string.Empty;
    public int MaxLevel { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ConfigVersion { get; init; } = string.Empty;
}

public sealed class BattlePassStateDto
{
    public string SeasonId { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Xp { get; init; }
    public string PassType { get; init; } = string.Empty;
    public List<BattlePassClaimedRewardDto> ClaimedRewards { get; init; } = [];
    public List<BattlePassClaimableRewardDto> ClaimableRewards { get; init; } = [];
}

public sealed class BattlePassProductsDto
{
    public string PremiumProductId { get; init; } = string.Empty;
    public string PlatinumProductId { get; init; } = string.Empty;
}

public sealed class BattlePassCurrentResponse
{
    public BattlePassSeasonDto? Season { get; init; }
    public BattlePassProductsDto? Products { get; init; }
    public BattlePassStateDto? UserState { get; init; }
    public List<BattlePassLevelDto> Levels { get; init; } = [];
    public string ServerTimeUtc { get; init; } = string.Empty;
}

public sealed class BattlePassScheduleSeasonDto
{
    public string Id { get; init; } = string.Empty;
    public string StartAtUtc { get; init; } = string.Empty;
    public string EndAtUtc { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class LiveOpsScheduleItemDto
{
    public string Id { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string StreamId { get; init; } = string.Empty;
    public string StartTimeUtc { get; init; } = string.Empty;
    public string EndTimeUtc { get; init; } = string.Empty;
    public Dictionary<string, string> CustomParams { get; init; } = new(StringComparer.Ordinal);
}

public sealed class BattlePassScheduleResponse
{
    public List<BattlePassScheduleSeasonDto> Seasons { get; init; } = [];
    public List<LiveOpsScheduleItemDto> Items { get; init; } = [];
    public string ServerTimeUtc { get; init; } = string.Empty;
}

public sealed class ClaimBattlePassRewardRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
    public int Level { get; init; }
    public string RewardTrack { get; init; } = string.Empty;
}

public sealed class ClaimAllBattlePassRewardsRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
}

public sealed class AddBattlePassXpRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public int Amount { get; init; }
}

public sealed class BattlePassClaimResponse
{
    public bool Success { get; init; }
    public List<BattlePassRewardDto> GrantedRewards { get; init; } = [];
    public BattlePassStateDto? BattlePass { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AddBattlePassXpResponse
{
    public bool Success { get; init; }
    public int AddedXp { get; init; }
    public BattlePassStateDto? BattlePass { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
