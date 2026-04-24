using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.BattlePass;

namespace GameServer.Api.Models.Dto.Admin;

public sealed class CreateBattlePassSeasonRequest
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string StartAtUtc { get; init; } = string.Empty;
    public string EndAtUtc { get; init; } = string.Empty;
    public int MaxLevel { get; init; }
    public string ConfigVersion { get; init; } = "v1";
    public string GoldProductId { get; init; } = string.Empty;
    public string PlatinumProductId { get; init; } = string.Empty;
}

public sealed class UpdateBattlePassSeasonRequest
{
    public string Title { get; init; } = string.Empty;
    public string StartAtUtc { get; init; } = string.Empty;
    public string EndAtUtc { get; init; } = string.Empty;
    public int MaxLevel { get; init; }
    public string ConfigVersion { get; init; } = "v1";
    public string GoldProductId { get; init; } = string.Empty;
    public string PlatinumProductId { get; init; } = string.Empty;
}

public sealed class BattlePassLevelAdminRequest
{
    public int Level { get; init; }
    public int XpRequired { get; init; }
    public List<string> DefaultRewardIds { get; init; } = [];
    public List<string> PremiumRewardIds { get; init; } = [];
    public List<string> PlatinumRewardIds { get; init; } = [];
}

public sealed class UpsertBattlePassLevelsRequest
{
    public List<BattlePassLevelAdminRequest> Levels { get; init; } = [];
}

public sealed class AdminGrantEntitlementRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
    public string PassType { get; init; } = string.Empty;
}

public sealed class AdminRevokeEntitlementRequest
{
    public string PlayerId { get; init; } = string.Empty;
    public string SeasonId { get; init; } = string.Empty;
    public string? PassType { get; init; }
}

public sealed class AdminPlayerBattlePassViewDto
{
    public string PlayerId { get; init; } = string.Empty;
    public BattlePassSeasonDto? CurrentSeason { get; init; }
    public BattlePassStateDto? CurrentState { get; init; }
    public List<UserBattlePassRecord> States { get; init; } = [];
    public List<UserEntitlementRecord> Entitlements { get; init; } = [];
}

public sealed class BattlePassAdminOperationResponse
{
    public bool Success { get; init; }
    public BattlePassSeasonRecord? Season { get; init; }
    public List<BattlePassLevelRecord> Levels { get; init; } = [];
    public UserEntitlementRecord? Entitlement { get; init; }
    public PurchaseRecord? Purchase { get; init; }
    public AdminPlayerBattlePassViewDto? PlayerState { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
