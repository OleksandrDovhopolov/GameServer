using GameServer.Api.Services.Interfaces;
using StackExchange.Redis;

namespace GameServer.Api.Services;

public sealed class RedisDistributedLockService : IDistributedLockService
{
    private static readonly TimeSpan AcquireRetryDelay = TimeSpan.FromMilliseconds(25);

    private const string ReleaseLockLua = """
                                          if redis.call('GET', KEYS[1]) == ARGV[1] then
                                            return redis.call('DEL', KEYS[1])
                                          end
                                          return 0
                                          """;

    private readonly IDatabase _database;
    private readonly ILogger<RedisDistributedLockService> _logger;

    public RedisDistributedLockService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedLockService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Lock key is required.", nameof(key));
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Lock ttl must be positive.");
        }

        var token = Guid.NewGuid().ToString("N");
        var redisKey = (RedisKey)$"lock:{key}";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var acquired = await _database.StringSetAsync(redisKey, token, ttl, when: When.NotExists).WaitAsync(ct);
            if (acquired)
            {
                return new Releaser(this, redisKey, token);
            }

            await Task.Delay(AcquireRetryDelay, ct);
        }
    }

    private async Task ReleaseAsync(RedisKey redisKey, string token)
    {
        try
        {
            _ = await _database.ScriptEvaluateAsync(ReleaseLockLua, [redisKey], [token]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release distributed lock key {LockKey}.", redisKey);
        }
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly RedisDistributedLockService _owner;
        private readonly RedisKey _redisKey;
        private readonly string _token;
        private int _disposed;

        public Releaser(RedisDistributedLockService owner, RedisKey redisKey, string token)
        {
            _owner = owner;
            _redisKey = redisKey;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await _owner.ReleaseAsync(_redisKey, _token);
        }
    }
}
