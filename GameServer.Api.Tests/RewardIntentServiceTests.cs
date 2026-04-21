using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardIntentServiceTests
{
    [Fact]
    public async Task CreateIntentAsync_WhenValid_CreatesPendingIntent()
    {
        var repository = new InMemoryRewardIntentRepository();
        var service = CreateService(repository, playerExists: true, nowUtcMs: 1_700_000_000_000);

        var response = await service.CreateIntentAsync("player_1", RewardIntentDefaults.SupportedRewardId, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.RewardIntentId);
        Assert.StartsWith("ri_", response.RewardIntentId!, StringComparison.Ordinal);

        var savedIntent = await repository.GetByIdAsync(response.RewardIntentId!, CancellationToken.None);
        Assert.NotNull(savedIntent);
        Assert.Equal(RewardIntentStatuses.Pending, savedIntent!.Status);
        Assert.Equal("player_1", savedIntent.PlayerId);
        Assert.Equal(RewardIntentDefaults.SupportedRewardId, savedIntent.RewardId);
        Assert.Equal(1_700_000_000_000 + (long)RewardIntentDefaults.IntentTtl.TotalMilliseconds, savedIntent.ExpiresAtUtc);
    }

    [Fact]
    public async Task CreateIntentAsync_WhenPlayerMissing_ReturnsPlayerNotFound()
    {
        var service = CreateService(new InMemoryRewardIntentRepository(), playerExists: false, nowUtcMs: 1_700_000_000_000);

        var response = await service.CreateIntentAsync("missing", RewardIntentDefaults.SupportedRewardId, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentErrorCodes.PlayerNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task CreateIntentAsync_WhenRewardUnsupported_ReturnsRewardNotSupported()
    {
        var service = CreateService(new InMemoryRewardIntentRepository(), playerExists: true, nowUtcMs: 1_700_000_000_000);

        var response = await service.CreateIntentAsync("player_1", "Gold", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentErrorCodes.RewardNotSupported, response.ErrorCode);
    }

    [Fact]
    public async Task GetIntentStatusAsync_WhenPending_ReturnsPending()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_pending",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 999_999_999_999
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.GetIntentStatusAsync("ri_pending", CancellationToken.None);

        Assert.Equal(RewardIntentStatuses.Pending, response.Status);
        Assert.Null(response.NewCrystalsBalance);
        Assert.Null(response.ErrorCode);
    }

    [Fact]
    public async Task GetIntentStatusAsync_WhenFulfilled_ReturnsBalance()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_fulfilled",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Fulfilled,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 2000,
            ExpiresAtUtc = 999_999_999_999,
            FulfilledAtUtc = 2000,
            NewCrystalsBalance = 2050
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.GetIntentStatusAsync("ri_fulfilled", CancellationToken.None);

        Assert.Equal(RewardIntentStatuses.Fulfilled, response.Status);
        Assert.Equal(2050, response.NewCrystalsBalance);
    }

    [Fact]
    public async Task GetIntentStatusAsync_WhenExpiredByLazyCheck_ReturnsExpired()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_expiring",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 2000
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 3000);

        var response = await service.GetIntentStatusAsync("ri_expiring", CancellationToken.None);

        Assert.Equal(RewardIntentStatuses.Expired, response.Status);
        Assert.Equal(RewardIntentErrorCodes.IntentExpired, response.ErrorCode);
    }

    [Fact]
    public async Task GetIntentStatusAsync_WhenNotFound_ReturnsIntentNotFound()
    {
        var service = CreateService(new InMemoryRewardIntentRepository(), playerExists: true, nowUtcMs: 10_000);

        var response = await service.GetIntentStatusAsync("missing", CancellationToken.None);

        Assert.Equal(RewardIntentStatuses.Failed, response.Status);
        Assert.Equal(RewardIntentErrorCodes.IntentNotFound, response.ErrorCode);
    }

    [Fact]
    public async Task ConfirmIntentFromAdCallbackAsync_WhenValid_UpdatesBalanceAndFulfills()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SetGems("player_1", 2000);
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_cb_ok",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 999_999_999_999
        });

        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.ConfirmIntentFromAdCallbackAsync(
            "levelplay",
            "ri_cb_ok",
            "event_1",
            "{}",
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(RewardIntentStatuses.Fulfilled, response.Status);
        Assert.Equal(2050, response.NewCrystalsBalance);
    }

    [Fact]
    public async Task ConfirmIntentFromAdCallbackAsync_WhenDuplicateEvent_DoesNotGrantTwice()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SetGems("player_1", 2000);
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_cb_dup",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 999_999_999_999
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var first = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_cb_dup", "event_1", "{}", CancellationToken.None);
        var second = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_cb_dup", "event_1", "{}", CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(RewardIntentErrorCodes.CallbackDuplicate, second.ErrorCode);
        Assert.Equal(2050, repository.GetGems("player_1"));
    }

    [Fact]
    public async Task ConfirmIntentFromAdCallbackAsync_WhenAlreadyFulfilled_DoesNotGrantTwice()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SetGems("player_1", 2000);
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_cb_fulfilled",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Fulfilled,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 2000,
            ExpiresAtUtc = 999_999_999_999,
            FulfilledAtUtc = 2000,
            NewCrystalsBalance = 2050
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.ConfirmIntentFromAdCallbackAsync(
            "levelplay",
            "ri_cb_fulfilled",
            "event_2",
            "{}",
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentErrorCodes.IntentAlreadyFulfilled, response.ErrorCode);
        Assert.Equal(2000, repository.GetGems("player_1"));
    }

    [Fact]
    public async Task ConfirmIntentFromAdCallbackAsync_WhenIntentExpired_DoesNotGrant()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SetGems("player_1", 2000);
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_cb_expired",
            PlayerId = "player_1",
            RewardId = RewardIntentDefaults.SupportedRewardId,
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1500
        });
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.ConfirmIntentFromAdCallbackAsync(
            "levelplay",
            "ri_cb_expired",
            "event_3",
            "{}",
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentStatuses.Expired, response.Status);
        Assert.Equal(RewardIntentErrorCodes.IntentExpired, response.ErrorCode);
        Assert.Equal(2000, repository.GetGems("player_1"));
    }

    [Fact]
    public async Task ConfirmIntentFromAdCallbackAsync_WhenGrantFails_ReturnsFailed()
    {
        var repository = new InMemoryRewardIntentRepository
        {
            ForcedConfirmResult = new RewardIntentConfirmResult
            {
                ResultCode = RewardIntentErrorCodes.RewardGrantFailed,
                Status = RewardIntentStatuses.Failed,
                ErrorMessage = "forced failure"
            }
        };
        var service = CreateService(repository, playerExists: true, nowUtcMs: 10_000);

        var response = await service.ConfirmIntentFromAdCallbackAsync(
            "levelplay",
            "ri_cb_fail",
            "event_4",
            "{}",
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentStatuses.Failed, response.Status);
        Assert.Equal(RewardIntentErrorCodes.RewardGrantFailed, response.ErrorCode);
    }

    private static RewardIntentService CreateService(InMemoryRewardIntentRepository repository, bool playerExists, long nowUtcMs)
    {
        return new RewardIntentService(
            repository,
            new FakePlayerEconomyRepository(playerExists),
            new FakeClock(nowUtcMs),
            NullLogger<RewardIntentService>.Instance);
    }

    private sealed class FakePlayerEconomyRepository : IPlayerEconomyRepository
    {
        private readonly bool _playerExists;

        public FakePlayerEconomyRepository(bool playerExists)
        {
            _playerExists = playerExists;
        }

        public Task<bool> PlayerExistsAsync(string playerId, CancellationToken ct)
        {
            return Task.FromResult(_playerExists);
        }
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(long nowUtcMs)
        {
            UtcNowUnixMilliseconds = nowUtcMs;
        }

        public long UtcNowUnixMilliseconds { get; }
    }

    private sealed class InMemoryRewardIntentRepository : IRewardIntentRepository
    {
        private readonly Dictionary<string, RewardIntentRecord> _intents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _gemsByPlayerId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _processedEvents = new(StringComparer.Ordinal);

        public RewardIntentConfirmResult? ForcedConfirmResult { get; init; }

        public Task<bool> CreateAsync(RewardIntentRecord intent, CancellationToken ct)
        {
            if (_intents.ContainsKey(intent.RewardIntentId))
            {
                return Task.FromResult(false);
            }

            _intents[intent.RewardIntentId] = intent;
            return Task.FromResult(true);
        }

        public Task<RewardIntentRecord?> GetByIdAsync(string rewardIntentId, CancellationToken ct)
        {
            _intents.TryGetValue(rewardIntentId, out var intent);
            return Task.FromResult(intent);
        }

        public Task<bool> ExpireIfPendingAsync(string rewardIntentId, long nowUtcMs, string errorCode, string errorMessage, CancellationToken ct)
        {
            if (!_intents.TryGetValue(rewardIntentId, out var intent))
            {
                return Task.FromResult(false);
            }

            if (!string.Equals(intent.Status, RewardIntentStatuses.Pending, StringComparison.Ordinal) || intent.ExpiresAtUtc > nowUtcMs)
            {
                return Task.FromResult(false);
            }

            _intents[rewardIntentId] = Clone(
                intent,
                status: RewardIntentStatuses.Expired,
                updatedAtUtc: nowUtcMs,
                errorCode: errorCode,
                errorMessage: errorMessage);
            return Task.FromResult(true);
        }

        public Task<RewardIntentConfirmResult> ConfirmFromCallbackAsync(
            string provider,
            string rewardIntentId,
            string externalEventId,
            string expectedRewardId,
            int rewardAmount,
            long nowUtcMs,
            CancellationToken ct)
        {
            if (ForcedConfirmResult is not null)
            {
                return Task.FromResult(ForcedConfirmResult);
            }

            var callbackKey = $"{provider}:{externalEventId}";
            if (_processedEvents.Contains(callbackKey))
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackDuplicate,
                    Status = RewardIntentStatuses.Failed,
                    ErrorMessage = "duplicate"
                });
            }

            if (!_intents.TryGetValue(rewardIntentId, out var intent))
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentNotFound,
                    Status = RewardIntentStatuses.Failed,
                    ErrorMessage = "not found"
                });
            }

            if (string.Equals(intent.Status, RewardIntentStatuses.Fulfilled, StringComparison.Ordinal))
            {
                _processedEvents.Add(callbackKey);
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentAlreadyFulfilled,
                    Status = RewardIntentStatuses.Fulfilled,
                    NewCrystalsBalance = intent.NewCrystalsBalance,
                    ErrorMessage = "already fulfilled"
                });
            }

            if (string.Equals(intent.Status, RewardIntentStatuses.Expired, StringComparison.Ordinal))
            {
                _processedEvents.Add(callbackKey);
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentExpired,
                    Status = RewardIntentStatuses.Expired,
                    ErrorMessage = "expired"
                });
            }

            if (!string.Equals(intent.Status, RewardIntentStatuses.Pending, StringComparison.Ordinal))
            {
                _processedEvents.Add(callbackKey);
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackInvalid,
                    Status = intent.Status,
                    ErrorMessage = "invalid"
                });
            }

            if (intent.ExpiresAtUtc <= nowUtcMs)
            {
                _intents[rewardIntentId] = Clone(
                    intent,
                    status: RewardIntentStatuses.Expired,
                    updatedAtUtc: nowUtcMs,
                    errorCode: RewardIntentErrorCodes.IntentExpired,
                    errorMessage: "expired");
                _processedEvents.Add(callbackKey);
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentExpired,
                    Status = RewardIntentStatuses.Expired,
                    ErrorMessage = "expired"
                });
            }

            if (!string.Equals(intent.RewardId, expectedRewardId, StringComparison.Ordinal))
            {
                _intents[rewardIntentId] = Clone(
                    intent,
                    status: RewardIntentStatuses.Rejected,
                    updatedAtUtc: nowUtcMs,
                    errorCode: RewardIntentErrorCodes.CallbackInvalid,
                    errorMessage: "reward mismatch");
                _processedEvents.Add(callbackKey);
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackInvalid,
                    Status = RewardIntentStatuses.Rejected,
                    ErrorMessage = "reward mismatch"
                });
            }

            _gemsByPlayerId.TryGetValue(intent.PlayerId, out var current);
            var next = current + rewardAmount;
            _gemsByPlayerId[intent.PlayerId] = next;
            _intents[rewardIntentId] = Clone(
                intent,
                status: RewardIntentStatuses.Fulfilled,
                updatedAtUtc: nowUtcMs,
                fulfilledAtUtc: nowUtcMs,
                newCrystalsBalance: next,
                errorCode: null,
                errorMessage: null);
            _processedEvents.Add(callbackKey);

            return Task.FromResult(new RewardIntentConfirmResult
            {
                ResultCode = "OK",
                Status = RewardIntentStatuses.Fulfilled,
                NewCrystalsBalance = next
            });
        }

        public void SeedIntent(RewardIntentRecord intent)
        {
            _intents[intent.RewardIntentId] = intent;
        }

        public void SetGems(string playerId, int value)
        {
            _gemsByPlayerId[playerId] = value;
        }

        public int GetGems(string playerId)
        {
            return _gemsByPlayerId.TryGetValue(playerId, out var value) ? value : 0;
        }

        private static RewardIntentRecord Clone(
            RewardIntentRecord source,
            string? status = null,
            long? updatedAtUtc = null,
            long? fulfilledAtUtc = null,
            int? newCrystalsBalance = null,
            string? errorCode = null,
            string? errorMessage = null)
        {
            return new RewardIntentRecord
            {
                RewardIntentId = source.RewardIntentId,
                PlayerId = source.PlayerId,
                RewardId = source.RewardId,
                Status = status ?? source.Status,
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = updatedAtUtc ?? source.UpdatedAtUtc,
                ExpiresAtUtc = source.ExpiresAtUtc,
                FulfilledAtUtc = fulfilledAtUtc ?? source.FulfilledAtUtc,
                NewCrystalsBalance = newCrystalsBalance ?? source.NewCrystalsBalance,
                ErrorCode = errorCode ?? source.ErrorCode,
                ErrorMessage = errorMessage ?? source.ErrorMessage,
                AdNetwork = source.AdNetwork,
                ExternalEventId = source.ExternalEventId
            };
        }
    }
}
