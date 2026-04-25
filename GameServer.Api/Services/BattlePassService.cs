using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class BattlePassService : IBattlePassService
{
    private const string CheatXpSource = "cheat";
    private const string NoActiveSeasonMessage = "No active battle pass season is available.";

    private static readonly string[] ClaimTrackOrder =
    [
        BattlePassRewardTracks.Default,
        BattlePassRewardTracks.Premium
    ];

    private readonly IPlayerEconomyRepository _playerEconomyRepository;
    private readonly IBattlePassSeasonRepository _seasonRepository;
    private readonly IBattlePassStateRepository _stateRepository;
    private readonly IEntitlementService _entitlementService;
    private readonly IRewardDefinitionRepository _rewardDefinitionRepository;
    private readonly IRewardApplicationService _rewardApplicationService;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ISystemClock _clock;
    private readonly ILiveOpsScheduleItemProvider _liveOpsScheduleItemProvider;
    private readonly ILogger<BattlePassService> _logger;

    public BattlePassService(
        IPlayerEconomyRepository playerEconomyRepository,
        IBattlePassSeasonRepository seasonRepository,
        IBattlePassStateRepository stateRepository,
        IEntitlementService entitlementService,
        IRewardDefinitionRepository rewardDefinitionRepository,
        IRewardApplicationService rewardApplicationService,
        IDistributedLockService distributedLockService,
        ISystemClock clock,
        ILiveOpsScheduleItemProvider liveOpsScheduleItemProvider,
        ILogger<BattlePassService> logger)
    {
        _playerEconomyRepository = playerEconomyRepository;
        _seasonRepository = seasonRepository;
        _stateRepository = stateRepository;
        _entitlementService = entitlementService;
        _rewardDefinitionRepository = rewardDefinitionRepository;
        _rewardApplicationService = rewardApplicationService;
        _distributedLockService = distributedLockService;
        _clock = clock;
        _liveOpsScheduleItemProvider = liveOpsScheduleItemProvider;
        _logger = logger;
    }

    public async Task<BattlePassCurrentResponse> GetCurrentAsync(string playerId, CancellationToken ct = default)
    {
        ValidatePlayerId(playerId);
        await EnsurePlayerExistsAsync(playerId, ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var seasons = await LoadNormalizedSeasonsAsync(nowUtcMs, ct);
        var activeSeason = seasons.FirstOrDefault(x => string.Equals(x.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal));
        if (activeSeason is null)
        {
            return new BattlePassCurrentResponse
            {
                Season = null,
                Products = null,
                UserState = null,
                Levels = [],
                ServerTimeUtc = UnixTimeHelper.ToUtcIsoString(nowUtcMs)
            };
        }

        var levels = await GetSeasonLevelsAsync(activeSeason.Id, ct);
        var state = await GetOrCreateStateAsync(playerId, activeSeason, levels, ct);
        var passType = await _entitlementService.GetBattlePassPassTypeAsync(playerId, activeSeason.Id, nowUtcMs, ct);

        return new BattlePassCurrentResponse
        {
            Season = ToSeasonDto(activeSeason),
            Products = new BattlePassProductsDto
            {
                PremiumProductId = activeSeason.PremiumProductId,
                PlatinumProductId = activeSeason.PlatinumProductId
            },
            UserState = await BuildStateDtoAsync(playerId, activeSeason.Id, state, levels, passType, ct),
            Levels = await BuildLevelDtosAsync(levels, ct),
            ServerTimeUtc = UnixTimeHelper.ToUtcIsoString(nowUtcMs)
        };
    }

    public async Task<BattlePassScheduleResponse> GetScheduleAsync(CancellationToken ct = default)
    {
        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var seasons = await LoadNormalizedSeasonsAsync(nowUtcMs, ct);
        var items = await _liveOpsScheduleItemProvider.GetItemsAsync(ct);

        return new BattlePassScheduleResponse
        {
            Seasons = seasons
                .Where(x => !string.Equals(x.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal) &&
                            !string.Equals(x.Status, BattlePassSeasonStatuses.Archived, StringComparison.Ordinal))
                .OrderBy(x => x.StartAtUtcMs)
                .Select(x => new BattlePassScheduleSeasonDto
                {
                    Id = x.Id,
                    StartAtUtc = UnixTimeHelper.ToUtcIsoString(x.StartAtUtcMs),
                    EndAtUtc = UnixTimeHelper.ToUtcIsoString(x.EndAtUtcMs),
                    Status = x.Status
                })
                .ToList(),
            Items = items
                .OrderBy(x => x.StartTimeUtcMs)
                .Select(x => new LiveOpsScheduleItemDto
                {
                    Id = x.Id,
                    EventType = x.EventType,
                    StreamId = x.StreamId,
                    StartTimeUtc = UnixTimeHelper.ToUtcIsoString(x.StartTimeUtcMs),
                    EndTimeUtc = UnixTimeHelper.ToUtcIsoString(x.EndTimeUtcMs),
                    CustomParams = new Dictionary<string, string>(x.CustomParams, StringComparer.Ordinal)
                })
                .ToList(),
            ServerTimeUtc = UnixTimeHelper.ToUtcIsoString(nowUtcMs)
        };
    }

    public async Task<BattlePassClaimResponse> ClaimAsync(ClaimBattlePassRewardRequest request, CancellationToken ct = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.PlayerId) ||
            string.IsNullOrWhiteSpace(request.SeasonId) ||
            string.IsNullOrWhiteSpace(request.RewardTrack) ||
            request.Level <= 0)
        {
            return BuildClaimFailure("playerId, seasonId, level and rewardTrack are required.");
        }

        await EnsurePlayerExistsAsync(request.PlayerId, ct);

        await using var stateLock = await _distributedLockService.AcquireAsync(
            $"bp:{request.PlayerId}:{request.SeasonId}",
            TimeSpan.FromSeconds(15),
            ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var season = await GetNormalizedSeasonByIdAsync(request.SeasonId, nowUtcMs, ct);
        if (season is null)
        {
            return BuildClaimFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        if (!string.Equals(season.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
        {
            return BuildClaimFailure("Season is not active.", BattlePassErrorCodes.SeasonNotActive);
        }

        var levels = await GetSeasonLevelsAsync(season.Id, ct);
        var state = await GetOrCreateStateAsync(request.PlayerId, season, levels, ct);
        if (state.Level < request.Level)
        {
            return BuildClaimFailure("Requested level is not unlocked.", BattlePassErrorCodes.LevelNotReached);
        }

        var passType = await _entitlementService.GetBattlePassPassTypeAsync(request.PlayerId, season.Id, nowUtcMs, ct);
        if (!IsTrackAllowed(passType, request.RewardTrack))
        {
            return BuildClaimFailure("Requested reward track is not available.", BattlePassErrorCodes.RewardTrackNotAllowed);
        }

        var level = levels.FirstOrDefault(x => x.Level == request.Level);
        if (level is null)
        {
            return BuildClaimFailure("Requested level was not found.", BattlePassErrorCodes.ValidationError);
        }

        var claimKey = BuildClaimKey(request.Level, request.RewardTrack);
        if (state.ClaimedRewards.Any(x => string.Equals(x.Key, claimKey, StringComparison.Ordinal)))
        {
            return BuildClaimFailure("Reward was already claimed.", BattlePassErrorCodes.RewardAlreadyClaimed);
        }

        var rewardIds = GetRewardIds(level, request.RewardTrack);
        var rewards = await BuildRewardDtosAsync(rewardIds, ct);
        var grantResult = await GrantRewardsAsync(request.PlayerId, rewardIds, ct);
        if (!grantResult.Success)
        {
            return BuildClaimFailure(grantResult.ErrorMessage ?? "Reward grant failed.", grantResult.ErrorCode ?? RewardErrorCodes.RewardApplicationFailed);
        }

        var updatedState = CloneStateWithClaims(
            state,
            state.ClaimedRewards.Append(new ClaimedBattlePassRewardRecord
            {
                Key = claimKey,
                Level = request.Level,
                RewardTrack = request.RewardTrack,
                ClaimedAtUtcMs = nowUtcMs
            }),
            state.Xp,
            levels);
        await _stateRepository.SaveAsync(updatedState, ct);

        _logger.LogInformation(
            "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, Level: {Level}, RewardTrack: {RewardTrack}",
            BattlePassTelemetry.RewardClaimed,
            request.PlayerId,
            season.Id,
            request.Level,
            request.RewardTrack);

        return new BattlePassClaimResponse
        {
            Success = true,
            GrantedRewards = rewards,
            BattlePass = await BuildStateDtoAsync(request.PlayerId, season.Id, updatedState, levels, passType, ct)
        };
    }

    public async Task<BattlePassClaimResponse> ClaimAllAsync(ClaimAllBattlePassRewardsRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlayerId) || string.IsNullOrWhiteSpace(request.SeasonId))
        {
            return BuildClaimFailure("playerId and seasonId are required.");
        }

        await EnsurePlayerExistsAsync(request.PlayerId, ct);

        await using var stateLock = await _distributedLockService.AcquireAsync(
            $"bp:{request.PlayerId}:{request.SeasonId}",
            TimeSpan.FromSeconds(15),
            ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var season = await GetNormalizedSeasonByIdAsync(request.SeasonId, nowUtcMs, ct);
        if (season is null)
        {
            return BuildClaimFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        if (!string.Equals(season.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
        {
            return BuildClaimFailure("Season is not active.", BattlePassErrorCodes.SeasonNotActive);
        }

        var levels = await GetSeasonLevelsAsync(season.Id, ct);
        var state = await GetOrCreateStateAsync(request.PlayerId, season, levels, ct);
        var passType = await _entitlementService.GetBattlePassPassTypeAsync(request.PlayerId, season.Id, nowUtcMs, ct);
        var claimableRewards = await BuildClaimableRewardsAsync(state, levels, passType, ct);

        var grantedRewards = new List<BattlePassRewardDto>();
        var claimedRecords = state.ClaimedRewards.ToList();
        foreach (var claimableReward in claimableRewards)
        {
            var rewardIds = claimableReward.Rewards.Select(x => x.RewardId).ToList();
            var grantResult = await GrantRewardsAsync(request.PlayerId, rewardIds, ct);
            if (!grantResult.Success)
            {
                return BuildClaimFailure(grantResult.ErrorMessage ?? "Reward grant failed.", grantResult.ErrorCode ?? RewardErrorCodes.RewardApplicationFailed);
            }

            grantedRewards.AddRange(claimableReward.Rewards);
            claimedRecords.Add(new ClaimedBattlePassRewardRecord
            {
                Key = BuildClaimKey(claimableReward.Level, claimableReward.RewardTrack),
                Level = claimableReward.Level,
                RewardTrack = claimableReward.RewardTrack,
                ClaimedAtUtcMs = nowUtcMs
            });
        }

        var updatedState = CloneStateWithClaims(state, claimedRecords, state.Xp, levels);
        await _stateRepository.SaveAsync(updatedState, ct);

        if (grantedRewards.Count > 0)
        {
            _logger.LogInformation(
                "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, Claims: {ClaimCount}",
                BattlePassTelemetry.RewardClaimed,
                request.PlayerId,
                season.Id,
                grantedRewards.Count);
        }

        return new BattlePassClaimResponse
        {
            Success = true,
            GrantedRewards = grantedRewards,
            BattlePass = await BuildStateDtoAsync(request.PlayerId, season.Id, updatedState, levels, passType, ct)
        };
    }

    public async Task<BattlePassStateDto> GetSeasonStateAsync(string playerId, string seasonId, CancellationToken ct = default)
    {
        ValidatePlayerId(playerId);
        await EnsurePlayerExistsAsync(playerId, ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var season = await GetNormalizedSeasonByIdAsync(seasonId, nowUtcMs, ct)
            ?? throw new KeyNotFoundException($"Season '{seasonId}' was not found.");
        var levels = await GetSeasonLevelsAsync(season.Id, ct);
        var state = await GetOrCreateStateAsync(playerId, season, levels, ct);
        var passType = await _entitlementService.GetBattlePassPassTypeAsync(playerId, season.Id, nowUtcMs, ct);

        return await BuildStateDtoAsync(playerId, season.Id, state, levels, passType, ct);
    }

    public async Task<BattlePassStateDto> AddXpToCurrentSeasonAsync(string playerId, int amount, CancellationToken ct = default)
    {
        ValidatePlayerId(playerId);
        if (amount <= 0)
        {
            throw new ArgumentException("amount must be positive.");
        }

        await EnsurePlayerExistsAsync(playerId, ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var seasons = await LoadNormalizedSeasonsAsync(nowUtcMs, ct);
        var activeSeason = seasons.FirstOrDefault(x => string.Equals(x.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal));
        if (activeSeason is null)
        {
            throw new InvalidOperationException(NoActiveSeasonMessage);
        }

        return await AddXpAsync(playerId, activeSeason.Id, amount, CheatXpSource, ct);
    }

    public async Task<BattlePassStateDto> AddXpAsync(string playerId, string seasonId, int amount, string source, CancellationToken ct = default)
    {
        ValidatePlayerId(playerId);
        if (string.IsNullOrWhiteSpace(source) || amount <= 0)
        {
            throw new ArgumentException("amount and source are required.");
        }

        await EnsurePlayerExistsAsync(playerId, ct);
        await using var stateLock = await _distributedLockService.AcquireAsync(
            $"bp:{playerId}:{seasonId}",
            TimeSpan.FromSeconds(15),
            ct);

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var season = await GetNormalizedSeasonByIdAsync(seasonId, nowUtcMs, ct)
            ?? throw new KeyNotFoundException($"Season '{seasonId}' was not found.");
        if (!string.Equals(season.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Battle pass season is not active.");
        }

        var levels = await GetSeasonLevelsAsync(season.Id, ct);
        var state = await GetOrCreateStateAsync(playerId, season, levels, ct);
        var nextXp = checked(state.Xp + amount);
        var updatedState = CloneStateWithClaims(state, state.ClaimedRewards, nextXp, levels);
        await _stateRepository.SaveAsync(updatedState, ct);

        _logger.LogInformation(
            "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, Amount: {Amount}, Source: {Source}, Xp: {Xp}",
            BattlePassTelemetry.XpAdded,
            playerId,
            season.Id,
            amount,
            source,
            updatedState.Xp);
        if (updatedState.Level > state.Level)
        {
            _logger.LogInformation(
                "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, PreviousLevel: {PreviousLevel}, NewLevel: {NewLevel}",
                BattlePassTelemetry.LevelUp,
                playerId,
                season.Id,
                state.Level,
                updatedState.Level);
        }

        var passType = await _entitlementService.GetBattlePassPassTypeAsync(playerId, season.Id, nowUtcMs, ct);
        return await BuildStateDtoAsync(playerId, season.Id, updatedState, levels, passType, ct);
    }

    private async Task EnsurePlayerExistsAsync(string playerId, CancellationToken ct)
    {
        var exists = await _playerEconomyRepository.PlayerExistsAsync(playerId, ct);
        if (!exists)
        {
            throw new KeyNotFoundException($"Player '{playerId}' was not found.");
        }
    }

    private async Task<IReadOnlyList<BattlePassSeasonRecord>> LoadNormalizedSeasonsAsync(long nowUtcMs, CancellationToken ct)
    {
        var seasons = await _seasonRepository.GetAllAsync(ct);
        var normalized = new List<BattlePassSeasonRecord>(seasons.Count);
        foreach (var season in seasons)
        {
            var next = BattlePassSeasonState.Normalize(season, nowUtcMs);
            if (!string.Equals(season.Status, next.Status, StringComparison.Ordinal))
            {
                await _seasonRepository.SaveAsync(next, ct);
                if (string.Equals(next.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
                {
                    _logger.LogInformation("{EventName} SeasonId: {SeasonId}", BattlePassTelemetry.SeasonStarted, next.Id);
                }
                else if (string.Equals(next.Status, BattlePassSeasonStatuses.Ended, StringComparison.Ordinal))
                {
                    _logger.LogInformation("{EventName} SeasonId: {SeasonId}", BattlePassTelemetry.SeasonEnded, next.Id);
                }
            }

            normalized.Add(next);
        }

        return normalized;
    }

    private async Task<BattlePassSeasonRecord?> GetNormalizedSeasonByIdAsync(string seasonId, long nowUtcMs, CancellationToken ct)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId, ct);
        if (season is null)
        {
            return null;
        }

        var normalized = BattlePassSeasonState.Normalize(season, nowUtcMs);
        if (!string.Equals(normalized.Status, season.Status, StringComparison.Ordinal))
        {
            await _seasonRepository.SaveAsync(normalized, ct);
            if (string.Equals(normalized.Status, BattlePassSeasonStatuses.Active, StringComparison.Ordinal))
            {
                _logger.LogInformation("{EventName} SeasonId: {SeasonId}", BattlePassTelemetry.SeasonStarted, normalized.Id);
            }
            else if (string.Equals(normalized.Status, BattlePassSeasonStatuses.Ended, StringComparison.Ordinal))
            {
                _logger.LogInformation("{EventName} SeasonId: {SeasonId}", BattlePassTelemetry.SeasonEnded, normalized.Id);
            }
        }

        return normalized;
    }

    private async Task<IReadOnlyList<BattlePassLevelRecord>> GetSeasonLevelsAsync(string seasonId, CancellationToken ct)
    {
        var levels = (await _seasonRepository.GetLevelsAsync(seasonId, ct))
            .OrderBy(x => x.Level)
            .ToList();
        if (levels.Count == 0)
        {
            throw new InvalidOperationException($"Season '{seasonId}' has no configured levels.");
        }

        return levels;
    }

    private async Task<UserBattlePassRecord> GetOrCreateStateAsync(
        string playerId,
        BattlePassSeasonRecord season,
        IReadOnlyList<BattlePassLevelRecord> levels,
        CancellationToken ct)
    {
        var existing = await _stateRepository.GetAsync(playerId, season.Id, ct);
        if (existing is null)
        {
            var nowUtcMs = _clock.UtcNowUnixMilliseconds;
            var created = new UserBattlePassRecord
            {
                PlayerId = playerId,
                SeasonId = season.Id,
                Xp = 0,
                Level = CalculateLevel(0, levels),
                ClaimedRewards = [],
                CreatedAtUtcMs = nowUtcMs,
                UpdatedAtUtcMs = nowUtcMs
            };
            await _stateRepository.SaveAsync(created, ct);
            _logger.LogInformation(
                "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}",
                BattlePassTelemetry.StateCreated,
                playerId,
                season.Id);
            return created;
        }

        var recalculatedLevel = CalculateLevel(existing.Xp, levels);
        if (recalculatedLevel == existing.Level)
        {
            return existing;
        }

        var normalized = CloneStateWithClaims(existing, existing.ClaimedRewards, existing.Xp, levels);
        await _stateRepository.SaveAsync(normalized, ct);
        return normalized;
    }

    private static UserBattlePassRecord CloneStateWithClaims(
        UserBattlePassRecord state,
        IEnumerable<ClaimedBattlePassRewardRecord> claimedRewards,
        int xp,
        IReadOnlyList<BattlePassLevelRecord> levels)
    {
        return new UserBattlePassRecord
        {
            PlayerId = state.PlayerId,
            SeasonId = state.SeasonId,
            Xp = xp,
            Level = CalculateLevel(xp, levels),
            ClaimedRewards = claimedRewards
                .OrderBy(x => x.Level)
                .ThenBy(x => x.RewardTrack, StringComparer.Ordinal)
                .ToList(),
            CreatedAtUtcMs = state.CreatedAtUtcMs,
            UpdatedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static int CalculateLevel(int xp, IReadOnlyList<BattlePassLevelRecord> levels)
    {
        if (levels.Count == 0)
        {
            return 1;
        }

        var unlocked = levels
            .Where(x => x.XpRequired <= xp)
            .Select(x => x.Level)
            .DefaultIfEmpty(levels[0].Level)
            .Max();
        return Math.Min(unlocked, levels.Max(x => x.Level));
    }

    private async Task<BattlePassStateDto> BuildStateDtoAsync(
        string playerId,
        string seasonId,
        UserBattlePassRecord state,
        IReadOnlyList<BattlePassLevelRecord> levels,
        string passType,
        CancellationToken ct)
    {
        return new BattlePassStateDto
        {
            SeasonId = seasonId,
            Level = state.Level,
            Xp = state.Xp,
            PassType = passType,
            ClaimedRewards = state.ClaimedRewards
                .Select(x => new BattlePassClaimedRewardDto
                {
                    Level = x.Level,
                    RewardTrack = x.RewardTrack,
                    ClaimedAtUtc = UnixTimeHelper.ToUtcIsoString(x.ClaimedAtUtcMs)
                })
                .ToList(),
            ClaimableRewards = await BuildClaimableRewardsAsync(state, levels, passType, ct)
        };
    }

    private async Task<List<BattlePassClaimableRewardDto>> BuildClaimableRewardsAsync(
        UserBattlePassRecord state,
        IReadOnlyList<BattlePassLevelRecord> levels,
        string passType,
        CancellationToken ct)
    {
        var claimableRewards = new List<BattlePassClaimableRewardDto>();
        var claimed = new HashSet<string>(state.ClaimedRewards.Select(x => x.Key), StringComparer.Ordinal);
        foreach (var level in levels.Where(x => x.Level <= state.Level))
        {
            foreach (var track in ClaimTrackOrder)
            {
                if (!IsTrackAllowed(passType, track))
                {
                    continue;
                }

                var claimKey = BuildClaimKey(level.Level, track);
                if (claimed.Contains(claimKey))
                {
                    continue;
                }

                var rewardIds = GetRewardIds(level, track);
                if (rewardIds.Count == 0)
                {
                    continue;
                }

                claimableRewards.Add(new BattlePassClaimableRewardDto
                {
                    Level = level.Level,
                    RewardTrack = track,
                    Rewards = await BuildRewardDtosAsync(rewardIds, ct)
                });
            }
        }

        return claimableRewards;
    }

    private async Task<List<BattlePassLevelDto>> BuildLevelDtosAsync(IReadOnlyList<BattlePassLevelRecord> levels, CancellationToken ct)
    {
        var result = new List<BattlePassLevelDto>(levels.Count);
        foreach (var level in levels)
        {
            result.Add(new BattlePassLevelDto
            {
                Level = level.Level,
                XpRequired = level.XpRequired,
                DefaultRewards = await BuildRewardDtosAsync(level.DefaultRewardIds, ct),
                PremiumRewards = await BuildRewardDtosAsync(level.PremiumRewardIds, ct)
            });
        }

        return result;
    }

    private async Task<List<BattlePassRewardDto>> BuildRewardDtosAsync(IEnumerable<string> rewardIds, CancellationToken ct)
    {
        var rewards = new List<BattlePassRewardDto>();
        foreach (var rewardId in rewardIds.Distinct(StringComparer.Ordinal))
        {
            var definition = await _rewardDefinitionRepository.GetByIdAsync(rewardId, ct);
            if (definition is null)
            {
                throw new InvalidOperationException($"Reward definition '{rewardId}' was not found.");
            }

            rewards.Add(ToRewardDto(definition));
        }

        return rewards;
    }

    private async Task<GrantRewardResponse> GrantRewardsAsync(string playerId, IEnumerable<string> rewardIds, CancellationToken ct)
    {
        GrantRewardResponse? lastResponse = null;
        foreach (var rewardId in rewardIds)
        {
            lastResponse = await _rewardApplicationService.GrantAsync(new GrantRewardCommand
            {
                PlayerId = playerId,
                RewardSource = EntitlementTypes.BattlePass,
                RewardId = rewardId
            }, ct);

            if (!lastResponse.Success)
            {
                return lastResponse;
            }
        }

        return lastResponse ?? new GrantRewardResponse
        {
            Success = true,
            RewardId = string.Empty
        };
    }

    private static BattlePassSeasonDto ToSeasonDto(BattlePassSeasonRecord season)
    {
        return new BattlePassSeasonDto
        {
            Id = season.Id,
            Title = season.Title,
            StartAtUtc = UnixTimeHelper.ToUtcIsoString(season.StartAtUtcMs),
            EndAtUtc = UnixTimeHelper.ToUtcIsoString(season.EndAtUtcMs),
            MaxLevel = season.MaxLevel,
            Status = season.Status,
            ConfigVersion = season.ConfigVersion
        };
    }

    private static BattlePassRewardDto ToRewardDto(RewardDefinition definition)
    {
        return new BattlePassRewardDto
        {
            RewardId = definition.RewardId
        };
    }

    private static List<string> GetRewardIds(BattlePassLevelRecord level, string rewardTrack)
    {
        return rewardTrack switch
        {
            BattlePassRewardTracks.Default => level.DefaultRewardIds,
            BattlePassRewardTracks.Premium => level.PremiumRewardIds,
            _ => []
        };
    }

    private static string BuildClaimKey(int level, string rewardTrack) => $"{level}:{rewardTrack}";

    private static bool IsTrackAllowed(string passType, string rewardTrack)
    {
        if (string.Equals(rewardTrack, BattlePassRewardTracks.Default, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(rewardTrack, BattlePassRewardTracks.Premium, StringComparison.Ordinal))
        {
            return string.Equals(passType, BattlePassPassTypes.Premium, StringComparison.Ordinal) ||
                   string.Equals(passType, BattlePassPassTypes.Platinum, StringComparison.Ordinal);
        }

        return false;
    }

    private static void ValidatePlayerId(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("PlayerId is required.", nameof(playerId));
        }
    }

    private static BattlePassClaimResponse BuildClaimFailure(
        string errorMessage,
        string errorCode = BattlePassErrorCodes.ValidationError)
    {
        return new BattlePassClaimResponse
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
