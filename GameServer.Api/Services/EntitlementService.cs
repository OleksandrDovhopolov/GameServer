using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class EntitlementService : IEntitlementService
{
    private readonly IEntitlementRepository _entitlementRepository;
    private readonly ISystemClock _clock;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IEntitlementRepository entitlementRepository,
        ISystemClock clock,
        ILogger<EntitlementService> logger)
    {
        _entitlementRepository = entitlementRepository;
        _clock = clock;
        _logger = logger;
    }

    public Task<IReadOnlyList<UserEntitlementRecord>> GetPlayerEntitlementsAsync(string playerId, CancellationToken ct = default)
    {
        return _entitlementRepository.GetByPlayerAsync(playerId, ct);
    }

    public async Task<string> GetBattlePassPassTypeAsync(string playerId, string seasonId, long nowUtcMs, CancellationToken ct = default)
    {
        var entitlement = await GetActiveBattlePassEntitlementAsync(playerId, seasonId, nowUtcMs, ct);
        if (entitlement is null)
        {
            return BattlePassPassTypes.None;
        }

        return GetPassType(entitlement);
    }

    public async Task<UserEntitlementRecord?> GetActiveBattlePassEntitlementAsync(
        string playerId,
        string seasonId,
        long nowUtcMs,
        CancellationToken ct = default)
    {
        var entitlements = await _entitlementRepository.GetByPlayerAsync(playerId, ct);
        UserEntitlementRecord? premium = null;
        UserEntitlementRecord? platinum = null;
        foreach (var entitlement in entitlements)
        {
            if (!string.Equals(entitlement.EntitlementType, EntitlementTypes.BattlePass, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(GetEffectiveStatus(entitlement, nowUtcMs), EntitlementStatuses.Active, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetSeasonId(entitlement, out var storedSeasonId) ||
                !string.Equals(storedSeasonId, seasonId, StringComparison.Ordinal))
            {
                continue;
            }

            var passType = GetPassType(entitlement);
            if (string.Equals(passType, BattlePassPassTypes.Platinum, StringComparison.Ordinal))
            {
                platinum = entitlement;
            }
            else if (string.Equals(passType, BattlePassPassTypes.Premium, StringComparison.Ordinal))
            {
                premium = entitlement;
            }
        }

        return platinum ?? premium;
    }

    public async Task<EntitlementMutationResult> GrantBattlePassEntitlementAsync(
        string playerId,
        string seasonId,
        string passType,
        string sourceType,
        string? sourcePurchaseId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerId) ||
            string.IsNullOrWhiteSpace(seasonId) ||
            !IsSupportedPassType(passType))
        {
            return BuildFailure("playerId, seasonId and passType are required.");
        }

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var entitlements = await _entitlementRepository.GetByPlayerAsync(playerId, ct);
        var activePremium = entitlements.FirstOrDefault(x =>
            string.Equals(GetEffectiveStatus(x, nowUtcMs), EntitlementStatuses.Active, StringComparison.Ordinal) &&
            MatchesSeason(x, seasonId) &&
            string.Equals(GetPassType(x), BattlePassPassTypes.Premium, StringComparison.Ordinal));
        var activePlatinum = entitlements.FirstOrDefault(x =>
            string.Equals(GetEffectiveStatus(x, nowUtcMs), EntitlementStatuses.Active, StringComparison.Ordinal) &&
            MatchesSeason(x, seasonId) &&
            string.Equals(GetPassType(x), BattlePassPassTypes.Platinum, StringComparison.Ordinal));

        if (string.Equals(passType, BattlePassPassTypes.Premium, StringComparison.Ordinal))
        {
            if (activePremium is not null || activePlatinum is not null)
            {
                return BuildFailure("Player already owns premium or better pass.", BattlePassErrorCodes.PassAlreadyOwned);
            }
        }

        if (string.Equals(passType, BattlePassPassTypes.Platinum, StringComparison.Ordinal))
        {
            if (activePlatinum is not null)
            {
                return BuildFailure("Player already owns platinum pass.", BattlePassErrorCodes.PassAlreadyOwned);
            }

            if (activePremium is not null)
            {
                var revokedPremium = CloneEntitlement(activePremium, EntitlementStatuses.Revoked, nowUtcMs);
                await _entitlementRepository.SaveAsync(revokedPremium, ct);
                _logger.LogInformation(
                    "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, PassType: {PassType}, EntitlementId: {EntitlementId}",
                    BattlePassTelemetry.EntitlementRevoked,
                    playerId,
                    seasonId,
                    BattlePassPassTypes.Premium,
                    revokedPremium.Id);
            }
        }

        var entitlement = new UserEntitlementRecord
        {
            Id = $"ent_{Guid.NewGuid():N}",
            PlayerId = playerId,
            EntitlementType = EntitlementTypes.BattlePass,
            EntitlementKey = $"battle_pass_{passType}_{seasonId}",
            SourcePurchaseId = sourcePurchaseId,
            SourceType = sourceType,
            Status = EntitlementStatuses.Active,
            StartsAtUtcMs = nowUtcMs,
            ExpiresAtUtcMs = null,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["seasonId"] = seasonId,
                ["passType"] = passType
            },
            CreatedAtUtcMs = nowUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        };

        await _entitlementRepository.SaveAsync(entitlement, ct);
        _logger.LogInformation(
            "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, PassType: {PassType}, EntitlementId: {EntitlementId}, SourceType: {SourceType}",
            BattlePassTelemetry.EntitlementGranted,
            playerId,
            seasonId,
            passType,
            entitlement.Id,
            sourceType);

        return new EntitlementMutationResult
        {
            Success = true,
            Entitlement = entitlement
        };
    }

    public async Task<EntitlementMutationResult> RevokeBattlePassEntitlementAsync(
        string playerId,
        string seasonId,
        string? passType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(seasonId))
        {
            return BuildFailure("playerId and seasonId are required.");
        }

        var nowUtcMs = _clock.UtcNowUnixMilliseconds;
        var entitlements = await _entitlementRepository.GetByPlayerAsync(playerId, ct);
        var matches = entitlements
            .Where(x =>
                MatchesSeason(x, seasonId) &&
                string.Equals(GetEffectiveStatus(x, nowUtcMs), EntitlementStatuses.Active, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(passType) || string.Equals(GetPassType(x), passType, StringComparison.Ordinal)))
            .ToList();
        if (matches.Count == 0)
        {
            return BuildFailure("Active entitlement was not found.", BattlePassErrorCodes.ValidationError);
        }

        UserEntitlementRecord? revoked = null;
        foreach (var entitlement in matches)
        {
            revoked = CloneEntitlement(entitlement, EntitlementStatuses.Revoked, nowUtcMs);
            await _entitlementRepository.SaveAsync(revoked, ct);
            _logger.LogInformation(
                "{EventName} PlayerId: {PlayerId}, SeasonId: {SeasonId}, PassType: {PassType}, EntitlementId: {EntitlementId}",
                BattlePassTelemetry.EntitlementRevoked,
                playerId,
                seasonId,
                GetPassType(revoked),
                revoked.Id);
        }

        return new EntitlementMutationResult
        {
            Success = true,
            Entitlement = revoked
        };
    }

    private static bool MatchesSeason(UserEntitlementRecord entitlement, string seasonId)
    {
        return TryGetSeasonId(entitlement, out var storedSeasonId) &&
               string.Equals(storedSeasonId, seasonId, StringComparison.Ordinal);
    }

    private static string GetEffectiveStatus(UserEntitlementRecord entitlement, long nowUtcMs)
    {
        if (!string.Equals(entitlement.Status, EntitlementStatuses.Active, StringComparison.Ordinal))
        {
            return entitlement.Status;
        }

        return entitlement.ExpiresAtUtcMs.HasValue && entitlement.ExpiresAtUtcMs.Value <= nowUtcMs
            ? EntitlementStatuses.Expired
            : entitlement.Status;
    }

    private static bool TryGetSeasonId(UserEntitlementRecord entitlement, out string seasonId)
    {
        if (entitlement.Metadata.TryGetValue("seasonId", out seasonId!) &&
            !string.IsNullOrWhiteSpace(seasonId))
        {
            return true;
        }

        seasonId = string.Empty;
        return false;
    }

    private static string GetPassType(UserEntitlementRecord entitlement)
    {
        return entitlement.Metadata.TryGetValue("passType", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : BattlePassPassTypes.None;
    }

    private static bool IsSupportedPassType(string passType)
    {
        return string.Equals(passType, BattlePassPassTypes.Premium, StringComparison.Ordinal) ||
               string.Equals(passType, BattlePassPassTypes.Platinum, StringComparison.Ordinal);
    }

    private static UserEntitlementRecord CloneEntitlement(UserEntitlementRecord source, string status, long nowUtcMs)
    {
        return new UserEntitlementRecord
        {
            Id = source.Id,
            PlayerId = source.PlayerId,
            EntitlementType = source.EntitlementType,
            EntitlementKey = source.EntitlementKey,
            SourcePurchaseId = source.SourcePurchaseId,
            SourceType = source.SourceType,
            Status = status,
            StartsAtUtcMs = source.StartsAtUtcMs,
            ExpiresAtUtcMs = source.ExpiresAtUtcMs,
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.Ordinal),
            CreatedAtUtcMs = source.CreatedAtUtcMs,
            UpdatedAtUtcMs = nowUtcMs
        };
    }

    private static EntitlementMutationResult BuildFailure(string errorMessage, string errorCode = BattlePassErrorCodes.ValidationError)
    {
        return new EntitlementMutationResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
