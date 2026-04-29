using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Models.Dto.Admin;
using GameServer.Api.Models.Dto.BattlePass;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;
using System.Text.Json.Nodes;

namespace GameServer.Api.Services;

public sealed class BattlePassAdminService : IBattlePassAdminService
{
    private readonly IBattlePassSeasonRepository _seasonRepository;
    private readonly IBattlePassStateRepository _stateRepository;
    private readonly IProductCatalogRepository _productCatalogRepository;
    private readonly IRewardDefinitionRepository _rewardDefinitionRepository;
    private readonly IEntitlementService _entitlementService;
    private readonly IPlayerEconomyRepository _playerEconomyRepository;
    private readonly IPlayerAggregateRepository _playerAggregateRepository;
    private readonly IPurchaseRepository _purchaseRepository;
    private readonly IBattlePassService _battlePassService;
    private readonly ISystemClock _clock;

    public BattlePassAdminService(
        IBattlePassSeasonRepository seasonRepository,
        IBattlePassStateRepository stateRepository,
        IProductCatalogRepository productCatalogRepository,
        IRewardDefinitionRepository rewardDefinitionRepository,
        IEntitlementService entitlementService,
        IPlayerEconomyRepository playerEconomyRepository,
        IPlayerAggregateRepository playerAggregateRepository,
        IPurchaseRepository purchaseRepository,
        IBattlePassService battlePassService,
        ISystemClock clock)
    {
        _seasonRepository = seasonRepository;
        _stateRepository = stateRepository;
        _productCatalogRepository = productCatalogRepository;
        _rewardDefinitionRepository = rewardDefinitionRepository;
        _entitlementService = entitlementService;
        _playerEconomyRepository = playerEconomyRepository;
        _playerAggregateRepository = playerAggregateRepository;
        _purchaseRepository = purchaseRepository;
        _battlePassService = battlePassService;
        _clock = clock;
    }

    public async Task<BattlePassAdminOperationResponse> CreateSeasonAsync(CreateBattlePassSeasonRequest request, CancellationToken ct = default)
    {
        var parseResult = TryBuildSeason(
            request.Id,
            request.Title,
            request.StartAtUtc,
            request.EndAtUtc,
            request.MaxLevel,
            request.ConfigVersion,
            request.PremiumProductId,
            request.PlatinumProductId,
            BattlePassSeasonStatuses.Draft,
            createdAtUtcMs: _clock.UtcNowUnixMilliseconds,
            updatedAtUtcMs: _clock.UtcNowUnixMilliseconds,
            out var season,
            out var errorMessage);
        if (!parseResult)
        {
            return BuildFailure(errorMessage!);
        }

        var existing = await _seasonRepository.GetByIdAsync(season!.Id, ct);
        if (existing is not null)
        {
            return BuildFailure($"Season '{season.Id}' already exists.", BattlePassErrorCodes.ValidationError);
        }

        await _seasonRepository.SaveAsync(season, ct);
        await UpsertProductCatalogAsync(season, ct);
        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = season
        };
    }

    public async Task<BattlePassAdminOperationResponse> UpdateSeasonAsync(string seasonId, UpdateBattlePassSeasonRequest request, CancellationToken ct = default)
    {
        var current = await _seasonRepository.GetByIdAsync(seasonId, ct);
        if (current is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        if (!string.Equals(current.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal))
        {
            return BuildFailure("Only draft seasons can be updated.", BattlePassErrorCodes.SeasonNotEditable);
        }

        var parseResult = TryBuildSeason(
            seasonId,
            request.Title,
            request.StartAtUtc,
            request.EndAtUtc,
            request.MaxLevel,
            request.ConfigVersion,
            request.PremiumProductId,
            request.PlatinumProductId,
            current.Status,
            current.CreatedAtUtcMs,
            _clock.UtcNowUnixMilliseconds,
            out var season,
            out var errorMessage);
        if (!parseResult)
        {
            return BuildFailure(errorMessage!);
        }

        var nextSeason = season!;
        await _seasonRepository.SaveAsync(nextSeason, ct);
        await UpsertProductCatalogAsync(nextSeason, ct);
        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = nextSeason
        };
    }

    public async Task<BattlePassAdminOperationResponse> UpsertLevelsAsync(string seasonId, UpsertBattlePassLevelsRequest request, CancellationToken ct = default)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        if (!string.Equals(season.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal))
        {
            return BuildFailure("Only draft seasons can be modified.", BattlePassErrorCodes.SeasonNotEditable);
        }

        if (request is null || request.Levels.Count == 0)
        {
            return BuildFailure("At least one level is required.");
        }

        var levels = request.Levels
            .OrderBy(x => x.Level)
            .Select(x => new BattlePassLevelRecord
            {
                SeasonId = seasonId,
                Level = x.Level,
                XpRequired = x.XpRequired,
                DefaultRewardId = x.DefaultRewardId.Trim(),
                PremiumRewardId = string.IsNullOrWhiteSpace(x.PremiumRewardId)
                    ? null
                    : x.PremiumRewardId.Trim()
            })
            .ToList();

        await _seasonRepository.SaveLevelsAsync(seasonId, levels, ct);
        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = season,
            Levels = levels
        };
    }

    public async Task<BattlePassAdminOperationResponse> PublishSeasonAsync(string seasonId, CancellationToken ct = default)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        if (!string.Equals(season.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal))
        {
            return BuildFailure("Only draft seasons can be published.", BattlePassErrorCodes.SeasonNotEditable);
        }

        var levels = (await _seasonRepository.GetLevelsAsync(seasonId, ct)).OrderBy(x => x.Level).ToList();
        var validationError = await ValidateSeasonAsync(season, levels, ct);
        if (validationError is not null)
        {
            return BuildFailure(validationError, BattlePassErrorCodes.SeasonValidationFailed);
        }

        var published = BattlePassSeasonState.Normalize(new BattlePassSeasonRecord
        {
            Id = season.Id,
            Title = season.Title,
            StartAtUtcMs = season.StartAtUtcMs,
            EndAtUtcMs = season.EndAtUtcMs,
            MaxLevel = season.MaxLevel,
            Status = BattlePassSeasonStatuses.Scheduled,
            ConfigVersion = season.ConfigVersion,
            PremiumProductId = season.PremiumProductId,
            PlatinumProductId = season.PlatinumProductId,
            CreatedAtUtcMs = season.CreatedAtUtcMs,
            UpdatedAtUtcMs = _clock.UtcNowUnixMilliseconds
        }, _clock.UtcNowUnixMilliseconds);

        await _seasonRepository.SaveAsync(published, ct);
        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = published,
            Levels = levels
        };
    }

    public async Task<BattlePassAdminOperationResponse> ArchiveSeasonAsync(string seasonId, CancellationToken ct = default)
    {
        var season = await _seasonRepository.GetByIdAsync(seasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        var archived = new BattlePassSeasonRecord
        {
            Id = season.Id,
            Title = season.Title,
            StartAtUtcMs = season.StartAtUtcMs,
            EndAtUtcMs = season.EndAtUtcMs,
            MaxLevel = season.MaxLevel,
            Status = BattlePassSeasonStatuses.Archived,
            ConfigVersion = season.ConfigVersion,
            PremiumProductId = season.PremiumProductId,
            PlatinumProductId = season.PlatinumProductId,
            CreatedAtUtcMs = season.CreatedAtUtcMs,
            UpdatedAtUtcMs = _clock.UtcNowUnixMilliseconds
        };

        await _seasonRepository.SaveAsync(archived, ct);
        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = archived
        };
    }

    public async Task<BattlePassAdminOperationResponse> RecreateSeasonAsync(RecreateBattlePassSeasonRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return BuildFailure("request is required.");
        }

        if (request.Season is null)
        {
            return BuildFailure("season is required.");
        }

        if (request.Levels.Count == 0)
        {
            return BuildFailure("At least one level is required.");
        }

        var bootstrapPlayerIds = request.BootstrapPlayerIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (bootstrapPlayerIds.Count != request.BootstrapPlayerIds.Count)
        {
            return BuildFailure("bootstrapPlayerIds must contain non-empty unique values.");
        }

        var createResponse = await CreateSeasonAsync(request.Season, ct);
        if (!createResponse.Success)
        {
            return createResponse;
        }

        var levelsResponse = await UpsertLevelsAsync(request.Season.Id, new UpsertBattlePassLevelsRequest
        {
            Levels = request.Levels
        }, ct);
        if (!levelsResponse.Success)
        {
            return levelsResponse;
        }

        var publishResponse = await PublishSeasonAsync(request.Season.Id, ct);
        if (!publishResponse.Success)
        {
            return publishResponse;
        }

        foreach (var playerId in bootstrapPlayerIds)
        {
            var aggregate = await _playerAggregateRepository.LoadAsync(playerId, ct);
            await _playerAggregateRepository.SaveAsync(aggregate, ct);
        }

        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Season = publishResponse.Season,
            Levels = publishResponse.Levels,
            BootstrappedPlayerIds = bootstrapPlayerIds
        };
    }

    public async Task<BattlePassAdminOperationResponse> ResetPlayerProgressAsync(ResetBattlePassPlayerRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlayerId) || string.IsNullOrWhiteSpace(request.SeasonId))
        {
            return BuildFailure("playerId and seasonId are required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(request.PlayerId, ct);
        if (!playerExists)
        {
            return BuildFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
        }

        var season = await _seasonRepository.GetByIdAsync(request.SeasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var existingState = await _stateRepository.GetAsync(request.PlayerId, request.SeasonId, ct);
        var createdAt = existingState?.CreatedAtUtcMs ?? nowUtcMs;
        var resetState = new UserBattlePassRecord
        {
            PlayerId = request.PlayerId,
            SeasonId = request.SeasonId,
            Xp = 0,
            Level = 0,
            ClaimedRewards = [],
            CreatedAtUtcMs = createdAt,
            UpdatedAtUtcMs = nowUtcMs
        };
        await _stateRepository.SaveAsync(resetState, ct);

        var revokeResult = await _entitlementService.RevokeBattlePassEntitlementAsync(
            request.PlayerId,
            request.SeasonId,
            null,
            ct);
        if (!revokeResult.Success &&
            !string.Equals(revokeResult.ErrorCode, BattlePassErrorCodes.ValidationError, StringComparison.Ordinal))
        {
            return BuildFailure(revokeResult.ErrorMessage ?? "Entitlement revoke failed.", revokeResult.ErrorCode ?? BattlePassErrorCodes.ValidationError);
        }

        var scheduleItemId = string.IsNullOrWhiteSpace(request.ScheduleItemId) ? request.SeasonId : request.ScheduleItemId.Trim();
        var aggregate = await _playerAggregateRepository.LoadAsync(request.PlayerId, ct);
        RemoveEventState(aggregate.RootDocument, scheduleItemId);
        await _playerAggregateRepository.SaveAsync(aggregate, ct);

        return new BattlePassAdminOperationResponse
        {
            Success = true
        };
    }

    public async Task<BattlePassAdminOperationResponse> GrantEntitlementAsync(AdminGrantEntitlementRequest request, CancellationToken ct = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.PlayerId) ||
            string.IsNullOrWhiteSpace(request.SeasonId) ||
            string.IsNullOrWhiteSpace(request.PassType))
        {
            return BuildFailure("playerId, seasonId and passType are required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(request.PlayerId, ct);
        if (!playerExists)
        {
            return BuildFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
        }

        var season = await _seasonRepository.GetByIdAsync(request.SeasonId, ct);
        if (season is null)
        {
            return BuildFailure("Season was not found.", BattlePassErrorCodes.SeasonNotFound);
        }

        var result = await _entitlementService.GrantBattlePassEntitlementAsync(
            request.PlayerId,
            request.SeasonId,
            request.PassType,
            EntitlementSourceTypes.Admin,
            null,
            ct);
        if (!result.Success)
        {
            return BuildFailure(result.ErrorMessage ?? "Entitlement grant failed.", result.ErrorCode ?? BattlePassErrorCodes.ValidationError);
        }

        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Entitlement = result.Entitlement
        };
    }

    public async Task<BattlePassAdminOperationResponse> RevokeEntitlementAsync(AdminRevokeEntitlementRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlayerId) || string.IsNullOrWhiteSpace(request.SeasonId))
        {
            return BuildFailure("playerId and seasonId are required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(request.PlayerId, ct);
        if (!playerExists)
        {
            return BuildFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
        }

        var result = await _entitlementService.RevokeBattlePassEntitlementAsync(
            request.PlayerId,
            request.SeasonId,
            request.PassType,
            ct);
        if (!result.Success)
        {
            return BuildFailure(result.ErrorMessage ?? "Entitlement revoke failed.", result.ErrorCode ?? BattlePassErrorCodes.ValidationError);
        }

        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Entitlement = result.Entitlement
        };
    }

    public async Task<BattlePassAdminOperationResponse> GetPlayerStateAsync(string playerId, string? seasonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return BuildFailure("playerId is required.");
        }

        var playerExists = await _playerEconomyRepository.PlayerExistsAsync(playerId, ct);
        if (!playerExists)
        {
            return BuildFailure("Player was not found.", BattlePassErrorCodes.PlayerNotFound);
        }

        BattlePassSeasonDto? currentSeason = null;
        BattlePassStateDto? currentState = null;
        if (!string.IsNullOrWhiteSpace(seasonId))
        {
            var season = await _seasonRepository.GetByIdAsync(seasonId, ct);
            if (season is not null)
            {
                currentSeason = new BattlePassSeasonDto
                {
                    Id = season.Id,
                    Title = season.Title,
                    StartAtUtc = UnixTimeHelper.ToUtcIsoString(season.StartAtUtcMs),
                    EndAtUtc = UnixTimeHelper.ToUtcIsoString(season.EndAtUtcMs),
                    MaxLevel = season.MaxLevel,
                    Status = season.Status,
                    ConfigVersion = season.ConfigVersion
                };
                currentState = await _battlePassService.GetSeasonStateAsync(playerId, seasonId, ct);
            }
        }
        else
        {
            var current = await _battlePassService.GetCurrentAsync(playerId, ct);
            currentSeason = current.Season;
            currentState = current.UserState;
        }

        return new BattlePassAdminOperationResponse
        {
            Success = true,
            PlayerState = new AdminPlayerBattlePassViewDto
            {
                PlayerId = playerId,
                CurrentSeason = currentSeason,
                CurrentState = currentState,
                States = (await _stateRepository.GetByPlayerAsync(playerId, ct)).ToList(),
                Entitlements = (await _entitlementService.GetPlayerEntitlementsAsync(playerId, ct)).ToList()
            }
        };
    }

    public async Task<BattlePassAdminOperationResponse> GetPurchaseAsync(string purchaseId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseId))
        {
            return BuildFailure("purchaseId is required.");
        }

        var purchase = await _purchaseRepository.GetByIdAsync(purchaseId, ct);
        if (purchase is null)
        {
            return BuildFailure("Purchase was not found.", BattlePassErrorCodes.PurchaseNotFound);
        }

        return new BattlePassAdminOperationResponse
        {
            Success = true,
            Purchase = purchase
        };
    }

    private async Task<string?> ValidateSeasonAsync(
        BattlePassSeasonRecord season,
        IReadOnlyList<BattlePassLevelRecord> levels,
        CancellationToken ct)
    {
        if (season.EndAtUtcMs <= season.StartAtUtcMs)
        {
            return "endAtUtc must be greater than startAtUtc.";
        }

        if (levels.Count != season.MaxLevel + 1)
        {
            return "Configured levels count must match maxLevel + 1.";
        }

        for (var index = 0; index < levels.Count; index++)
        {
            var expectedLevel = index;
            if (levels[index].Level != expectedLevel)
            {
                return "Levels must be contiguous and start from 0.";
            }
        }

        var rewardIds = levels
            .SelectMany(x => new[] { x.DefaultRewardId, x.PremiumRewardId })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var rewardId in rewardIds)
        {
            var definition = await _rewardDefinitionRepository.GetByIdAsync(rewardId, ct);
            if (definition is null)
            {
                return $"Reward definition '{rewardId}' was not found.";
            }
        }

        if (await _productCatalogRepository.GetByIdAsync(season.PremiumProductId, ct) is null ||
            await _productCatalogRepository.GetByIdAsync(season.PlatinumProductId, ct) is null)
        {
            return "Battle pass products must exist in catalog.";
        }

        if (levels.Any(x => string.IsNullOrWhiteSpace(x.DefaultRewardId)))
        {
            return "Each level must define defaultRewardId.";
        }

        var otherPublishedSeasons = (await _seasonRepository.GetAllAsync(ct))
            .Where(x =>
                !string.Equals(x.Id, season.Id, StringComparison.Ordinal) &&
                !string.Equals(x.Status, BattlePassSeasonStatuses.Draft, StringComparison.Ordinal) &&
                !string.Equals(x.Status, BattlePassSeasonStatuses.Archived, StringComparison.Ordinal))
            .ToList();
        foreach (var otherSeason in otherPublishedSeasons)
        {
            if (RangesOverlap(season.StartAtUtcMs, season.EndAtUtcMs, otherSeason.StartAtUtcMs, otherSeason.EndAtUtcMs))
            {
                return $"Season overlaps with '{otherSeason.Id}'.";
            }
        }

        return null;
    }

    private async Task UpsertProductCatalogAsync(BattlePassSeasonRecord season, CancellationToken ct)
    {
        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        await _productCatalogRepository.SaveAsync(new CatalogProductRecord
        {
            ProductId = season.PremiumProductId,
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = season.Id,
                ["passType"] = BattlePassPassTypes.Premium
            },
            CreatedAtUtcMs = nowUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        }, ct);

        await _productCatalogRepository.SaveAsync(new CatalogProductRecord
        {
            ProductId = season.PlatinumProductId,
            Platform = PurchasePlatforms.GooglePlay,
            PurchaseType = PurchaseTypes.BattlePass,
            EntitlementType = EntitlementTypes.BattlePass,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = season.Id,
                ["passType"] = BattlePassPassTypes.Platinum
            },
            CreatedAtUtcMs = nowUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        }, ct);
    }

    private static bool TryBuildSeason(
        string id,
        string title,
        string startAtUtc,
        string endAtUtc,
        int maxLevel,
        string configVersion,
        string premiumProductId,
        string platinumProductId,
        string status,
        long createdAtUtcMs,
        long updatedAtUtcMs,
        out BattlePassSeasonRecord? season,
        out string? errorMessage)
    {
        season = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(startAtUtc) ||
            string.IsNullOrWhiteSpace(endAtUtc) ||
            string.IsNullOrWhiteSpace(configVersion) ||
            string.IsNullOrWhiteSpace(premiumProductId) ||
            string.IsNullOrWhiteSpace(platinumProductId) ||
            maxLevel <= 0)
        {
            errorMessage = "All season fields are required and maxLevel must be positive.";
            return false;
        }

        if (!DateTimeOffset.TryParse(startAtUtc, out var parsedStart) ||
            !DateTimeOffset.TryParse(endAtUtc, out var parsedEnd))
        {
            errorMessage = "startAtUtc and endAtUtc must be valid UTC timestamps.";
            return false;
        }

        if (parsedStart.Offset != TimeSpan.Zero || parsedEnd.Offset != TimeSpan.Zero)
        {
            errorMessage = "startAtUtc and endAtUtc must be provided in UTC.";
            return false;
        }

        season = new BattlePassSeasonRecord
        {
            Id = id,
            Title = title,
            StartAtUtcMs = parsedStart.ToUnixTimeMilliseconds(),
            EndAtUtcMs = parsedEnd.ToUnixTimeMilliseconds(),
            MaxLevel = maxLevel,
            Status = status,
            ConfigVersion = configVersion,
            PremiumProductId = premiumProductId,
            PlatinumProductId = platinumProductId,
            CreatedAtUtcMs = createdAtUtcMs,
            UpdatedAtUtcMs = updatedAtUtcMs
        };
        return true;
    }

    private static bool RangesOverlap(long leftStart, long leftEnd, long rightStart, long rightEnd)
    {
        return leftStart < rightEnd && rightStart < leftEnd;
    }

    private static void RemoveEventState(JsonObject root, string scheduleItemId)
    {
        if (!root.TryGetPropertyValue("EventStates", out var eventStatesNode) || eventStatesNode is not JsonArray eventStates)
        {
            return;
        }

        for (var index = eventStates.Count - 1; index >= 0; index--)
        {
            if (eventStates[index] is not JsonObject state)
            {
                continue;
            }

            if (!state.TryGetPropertyValue("ScheduleItemId", out var idNode) || idNode is null)
            {
                continue;
            }

            var value = idNode.GetValue<string>();
            if (string.Equals(value, scheduleItemId, StringComparison.Ordinal))
            {
                eventStates.RemoveAt(index);
            }
        }
    }

    private static BattlePassAdminOperationResponse BuildFailure(
        string errorMessage,
        string errorCode = BattlePassErrorCodes.ValidationError)
    {
        return new BattlePassAdminOperationResponse
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
