using System.Text.Json;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Repositories.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Repositories;

public sealed class RedisEntitlementRepository : IEntitlementRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabase _database;

    public RedisEntitlementRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<UserEntitlementRecord?> GetByIdAsync(string entitlementId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetEntitlementKey(entitlementId)).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<UserEntitlementRecord>(raw!, JsonOptions);
    }

    public async Task<IReadOnlyList<UserEntitlementRecord>> GetByPlayerAsync(string playerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entitlementIds = await _database.SetMembersAsync(GetPlayerEntitlementsIndexKey(playerId)).WaitAsync(ct);
        if (entitlementIds.Length == 0)
        {
            return [];
        }

        var values = await _database.StringGetAsync(entitlementIds.Select(x => (RedisKey)GetEntitlementKey(x!)).ToArray()).WaitAsync(ct);
        return values
            .Where(x => !x.IsNullOrEmpty)
            .Select(x => JsonSerializer.Deserialize<UserEntitlementRecord>(x!, JsonOptions))
            .Where(x => x is not null)
            .Cast<UserEntitlementRecord>()
            .ToList();
    }

    public async Task SaveAsync(UserEntitlementRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = _database.CreateTransaction();
        _ = transaction.StringSetAsync(GetEntitlementKey(record.Id), JsonSerializer.Serialize(record, JsonOptions));
        _ = transaction.SetAddAsync(GetPlayerEntitlementsIndexKey(record.PlayerId), record.Id);
        _ = await transaction.ExecuteAsync().WaitAsync(ct);
    }

    private static string GetEntitlementKey(string entitlementId) => $"entitlement:{entitlementId}";
    private static string GetPlayerEntitlementsIndexKey(string playerId) => $"entitlement:user:{playerId}";
}

public sealed class RedisPurchaseRepository : IPurchaseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabase _database;

    public RedisPurchaseRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<PurchaseRecord?> GetByIdAsync(string purchaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetPurchaseKey(purchaseId)).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<PurchaseRecord>(raw!, JsonOptions);
    }

    public async Task<PurchaseRecord?> GetByTokenHashAsync(string purchaseTokenHash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var purchaseId = await _database.StringGetAsync(GetPurchaseTokenIndexKey(purchaseTokenHash)).WaitAsync(ct);
        if (purchaseId.IsNullOrEmpty)
        {
            return null;
        }

        return await GetByIdAsync(purchaseId!, ct);
    }

    public async Task<bool> TryCreateTokenIndexAsync(string purchaseTokenHash, string purchaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _database.StringSetAsync(
                GetPurchaseTokenIndexKey(purchaseTokenHash),
                purchaseId,
                when: When.NotExists)
            .WaitAsync(ct);
    }

    public async Task SaveAsync(PurchaseRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _database.StringSetAsync(GetPurchaseKey(record.Id), JsonSerializer.Serialize(record, JsonOptions)).WaitAsync(ct);
    }

    private static string GetPurchaseKey(string purchaseId) => $"purchase:{purchaseId}";
    private static string GetPurchaseTokenIndexKey(string purchaseTokenHash) => $"purchase:token:{purchaseTokenHash}";
}

public sealed class RedisProductCatalogRepository : IProductCatalogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabase _database;

    public RedisProductCatalogRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<CatalogProductRecord?> GetByIdAsync(string productId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await _database.StringGetAsync(GetProductKey(productId)).WaitAsync(ct);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<CatalogProductRecord>(raw!, JsonOptions);
    }

    public async Task SaveAsync(CatalogProductRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _database.StringSetAsync(GetProductKey(record.ProductId), JsonSerializer.Serialize(record, JsonOptions)).WaitAsync(ct);
    }

    private static string GetProductKey(string productId) => $"catalog:product:{productId}";
}
