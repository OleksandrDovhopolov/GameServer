using GameServer.Api.Models.Domain.BattlePass;

namespace GameServer.Api.Repositories.Interfaces;

public interface IBattlePassSeasonRepository
{
    Task<BattlePassSeasonRecord?> GetByIdAsync(string seasonId, CancellationToken ct);
    Task<IReadOnlyList<BattlePassSeasonRecord>> GetAllAsync(CancellationToken ct);
    Task SaveAsync(BattlePassSeasonRecord season, CancellationToken ct);
    Task<IReadOnlyList<BattlePassLevelRecord>> GetLevelsAsync(string seasonId, CancellationToken ct);
    Task SaveLevelsAsync(string seasonId, IReadOnlyList<BattlePassLevelRecord> levels, CancellationToken ct);
}

public interface IBattlePassStateRepository
{
    Task<UserBattlePassRecord?> GetAsync(string playerId, string seasonId, CancellationToken ct);
    Task<IReadOnlyList<UserBattlePassRecord>> GetByPlayerAsync(string playerId, CancellationToken ct);
    Task SaveAsync(UserBattlePassRecord record, CancellationToken ct);
}

public interface IEntitlementRepository
{
    Task<UserEntitlementRecord?> GetByIdAsync(string entitlementId, CancellationToken ct);
    Task<IReadOnlyList<UserEntitlementRecord>> GetByPlayerAsync(string playerId, CancellationToken ct);
    Task SaveAsync(UserEntitlementRecord record, CancellationToken ct);
}

public interface IPurchaseRepository
{
    Task<PurchaseRecord?> GetByIdAsync(string purchaseId, CancellationToken ct);
    Task<PurchaseRecord?> GetByTokenHashAsync(string purchaseTokenHash, CancellationToken ct);
    Task<bool> TryCreateTokenIndexAsync(string purchaseTokenHash, string purchaseId, CancellationToken ct);
    Task SaveAsync(PurchaseRecord record, CancellationToken ct);
}

public interface IProductCatalogRepository
{
    Task<CatalogProductRecord?> GetByIdAsync(string productId, CancellationToken ct);
    Task SaveAsync(CatalogProductRecord record, CancellationToken ct);
}
