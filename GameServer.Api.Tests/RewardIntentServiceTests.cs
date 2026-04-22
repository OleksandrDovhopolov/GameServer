using GameServer.Api.Models.Domain.RewardIntents;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Models.Dto.RewardIntents;
using GameServer.Api.Models.Dto.Rewards;
using GameServer.Api.Repositories.Interfaces;
using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardIntentServiceTests
{
    [Fact]
    public async Task CreateIntent_WhenRewardExistsInDefinitions_CreatesPendingIntent()
    {
        var repository = new InMemoryRewardIntentRepository();
        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses: []);

        var response = await service.CreateIntentAsync("player_1", "Gems", CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.RewardIntentId);
        var saved = await repository.GetByIdAsync(response.RewardIntentId!, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(RewardIntentStatuses.Pending, saved!.Status);
        Assert.Equal("Gems", saved.RewardId);
    }

    [Fact]
    public async Task CreateIntent_WhenRewardMissingInDefinitions_ReturnsRewardNotSupported()
    {
        var service = CreateService(
            new InMemoryRewardIntentRepository(),
            supportedRewardIds: ["Gold"],
            playerExists: true,
            grantResponses: []);

        var response = await service.CreateIntentAsync("player_1", "Gems", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentErrorCodes.RewardNotSupported, response.ErrorCode);
    }

    [Fact]
    public async Task GetStatus_WhenFulfilled_ReturnsStatusOnlyContract()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_status",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Fulfilled,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 2000,
            ExpiresAtUtc = 5000,
            FulfilledAtUtc = 2000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses: []);

        var response = await service.GetIntentStatusAsync("ri_status", CancellationToken.None);

        Assert.Equal("fulfilled", response.Status);
    }

    [Fact]
    public async Task Callback_WhenGrantSucceeds_FinalizesFulfilled()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_ok",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "Gems" }
            ]);

        var response = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_ok", "evt_1", "{}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("fulfilled", response.Status);
    }

    [Fact]
    public async Task Callback_WhenAlreadySucceededEvent_ReturnsCallbackDuplicate()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_dup",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "Gems" }
            ]);

        var first = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_dup", "evt_1", "{}", CancellationToken.None);
        var second = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_dup", "evt_1", "{}", CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(RewardIntentErrorCodes.CallbackDuplicate, second.ErrorCode);
    }

    [Fact]
    public async Task Callback_WhenGrantFailed_RetryWithSameEventIdCanSucceed()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_retry",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse
                {
                    Success = false,
                    RewardId = "Gems",
                    ErrorCode = RewardErrorCodes.RewardApplicationFailed,
                    ErrorMessage = "temporary"
                },
                new GrantRewardResponse { Success = true, RewardId = "Gems" }
            ]);

        var first = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_retry", "evt_7", "{}", CancellationToken.None);
        var second = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_retry", "evt_7", "{}", CancellationToken.None);

        Assert.False(first.Success);
        Assert.Equal("failed", first.Status);
        Assert.True(second.Success);
        Assert.Equal("fulfilled", second.Status);
    }

    [Fact]
    public async Task Callback_WhenExpiredBeforeClaim_ReturnsExpired()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_expired",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1100
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses: [],
            nowUtcMs: 2000);

        var response = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_expired", "evt_8", "{}", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal("expired", response.Status);
        Assert.Equal(RewardIntentErrorCodes.IntentExpired, response.ErrorCode);
    }

    [Fact]
    public async Task Callback_WhenIntentAlreadyFulfilled_ReturnsAlreadyFulfilledWithoutRegrant()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_fulfilled",
            PlayerId = "player_1",
            RewardId = "Gems",
            Status = RewardIntentStatuses.Fulfilled,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000,
            FulfilledAtUtc = 1200
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["Gems"],
            playerExists: true,
            grantResponses: []);

        var response = await service.ConfirmIntentFromAdCallbackAsync("levelplay", "ri_fulfilled", "evt_9", "{}", CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(RewardIntentErrorCodes.IntentAlreadyFulfilled, response.ErrorCode);
    }

    [Fact]
    public async Task CallbackGet_WhenResourceRewardFulfilled_ReturnsResourceDelta()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_get_resource",
            PlayerId = "player_1",
            RewardId = "reward_resource",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["reward_resource"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "reward_resource" }
            ],
            rewardDefinitions: new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_resource"] = new()
                {
                    RewardId = "reward_resource",
                    Source = "grant",
                    Parts = [new RewardPart { Kind = RewardKind.Resource, TargetId = "Gold", Amount = 25 }]
                }
            });

        var response = await service.ConfirmIntentFromAdCallbackGetAsync("levelplay", "ri_get_resource", "evt_100", "{}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(RewardIntentStatuses.Fulfilled, response.Status);
        Assert.NotNull(response.ActionResult);
        Assert.Equal("reward_resource", response.ActionResult!.RewardId);
        Assert.Equal(25, response.ActionResult.ResourceDelta["Gold"]);
        Assert.Empty(response.ActionResult.InventoryItems);
        Assert.Null(response.ActionResult.WheelSpinId);
    }

    [Fact]
    public async Task CallbackGet_WhenInventoryRewardFulfilled_ReturnsInventoryItems()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_get_inventory",
            PlayerId = "player_1",
            RewardId = "reward_inventory",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["reward_inventory"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "reward_inventory" }
            ],
            rewardDefinitions: new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_inventory"] = new()
                {
                    RewardId = "reward_inventory",
                    Source = "grant",
                    Parts = [new RewardPart { Kind = RewardKind.InventoryItem, TargetId = "Emerald_Pack", Amount = 2 }]
                }
            });

        var response = await service.ConfirmIntentFromAdCallbackGetAsync("levelplay", "ri_get_inventory", "evt_101", "{}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.ActionResult);
        var item = Assert.Single(response.ActionResult!.InventoryItems);
        Assert.Equal("Emerald_Pack", item.ItemId);
        Assert.Equal(2, item.Amount);
        Assert.Empty(response.ActionResult.ResourceDelta);
        Assert.Null(response.ActionResult.WheelSpinId);
    }

    [Fact]
    public async Task CallbackGet_WhenFreeSpinRewardFulfilled_ReturnsWheelSpinId()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_get_spin",
            PlayerId = "player_1",
            RewardId = "reward_free_spin",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["reward_free_spin"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "reward_free_spin" }
            ],
            rewardDefinitions: new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_free_spin"] = new()
                {
                    RewardId = "reward_free_spin",
                    Source = "grant",
                    Parts = [new RewardPart { Kind = RewardKind.FreeSpin, TargetId = "FortuneWheel", Amount = 1 }]
                }
            });

        var response = await service.ConfirmIntentFromAdCallbackGetAsync("levelplay", "ri_get_spin", "evt_102", "{}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.ActionResult);
        Assert.NotNull(response.ActionResult!.WheelSpinId);
        Assert.StartsWith("ws_", response.ActionResult.WheelSpinId, StringComparison.Ordinal);
        Assert.Equal(35, response.ActionResult.WheelSpinId!.Length);
        Assert.Empty(response.ActionResult.ResourceDelta);
        Assert.Empty(response.ActionResult.InventoryItems);
    }

    [Fact]
    public async Task CallbackGet_WhenMixedRewardFulfilled_ReturnsAllActionSections()
    {
        var repository = new InMemoryRewardIntentRepository();
        repository.SeedIntent(new RewardIntentRecord
        {
            RewardIntentId = "ri_get_mixed",
            PlayerId = "player_1",
            RewardId = "reward_mixed",
            Status = RewardIntentStatuses.Pending,
            CreatedAtUtc = 1000,
            UpdatedAtUtc = 1000,
            ExpiresAtUtc = 1_000_000
        });

        var service = CreateService(
            repository,
            supportedRewardIds: ["reward_mixed"],
            playerExists: true,
            grantResponses:
            [
                new GrantRewardResponse { Success = true, RewardId = "reward_mixed" }
            ],
            rewardDefinitions: new Dictionary<string, RewardDefinition>(StringComparer.Ordinal)
            {
                ["reward_mixed"] = new()
                {
                    RewardId = "reward_mixed",
                    Source = "grant",
                    Parts =
                    [
                        new RewardPart { Kind = RewardKind.Resource, TargetId = "Gems", Amount = 3 },
                        new RewardPart { Kind = RewardKind.InventoryItem, TargetId = "Ruby_Pack", Amount = 1 },
                        new RewardPart { Kind = RewardKind.FreeSpin, TargetId = "FortuneWheel", Amount = 1 }
                    ]
                }
            });

        var response = await service.ConfirmIntentFromAdCallbackGetAsync("levelplay", "ri_get_mixed", "evt_103", "{}", CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.ActionResult);
        Assert.Equal(3, response.ActionResult!.ResourceDelta["Gems"]);
        var item = Assert.Single(response.ActionResult.InventoryItems);
        Assert.Equal("Ruby_Pack", item.ItemId);
        Assert.Equal(1, item.Amount);
        Assert.StartsWith("ws_", response.ActionResult.WheelSpinId, StringComparison.Ordinal);
    }

    private static RewardIntentService CreateService(
        InMemoryRewardIntentRepository repository,
        IReadOnlyCollection<string> supportedRewardIds,
        bool playerExists,
        IReadOnlyList<GrantRewardResponse> grantResponses,
        long nowUtcMs = 5_000,
        IReadOnlyDictionary<string, RewardDefinition>? rewardDefinitions = null)
    {
        return new RewardIntentService(
            repository,
            new FakePlayerEconomyRepository(playerExists),
            new FakeRewardDefinitionRepository(supportedRewardIds, rewardDefinitions),
            new QueueRewardApplicationService(grantResponses),
            new FakeClock(nowUtcMs),
            NullLogger<RewardIntentService>.Instance);
    }

    private sealed class FakeClock : ISystemClock
    {
        public FakeClock(long utcNowUnixMilliseconds)
        {
            UtcNowUnixMilliseconds = utcNowUnixMilliseconds;
        }

        public long UtcNowUnixMilliseconds { get; }
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

    private sealed class FakeRewardDefinitionRepository : IRewardDefinitionRepository
    {
        private readonly HashSet<string> _supportedRewardIds;
        private readonly IReadOnlyDictionary<string, RewardDefinition> _definitions;

        public FakeRewardDefinitionRepository(
            IReadOnlyCollection<string> supportedRewardIds,
            IReadOnlyDictionary<string, RewardDefinition>? definitions)
        {
            _supportedRewardIds = new HashSet<string>(supportedRewardIds, StringComparer.Ordinal);
            _definitions = definitions ?? new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);
        }

        public Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
        {
            if (_definitions.TryGetValue(rewardId, out var definition))
            {
                return Task.FromResult<RewardDefinition?>(definition);
            }

            if (_supportedRewardIds.Contains(rewardId))
            {
                return Task.FromResult<RewardDefinition?>(new RewardDefinition
                {
                    RewardId = rewardId,
                    Source = "grant",
                    Parts = []
                });
            }

            return Task.FromResult<RewardDefinition?>(null);
        }
    }

    private sealed class QueueRewardApplicationService : IRewardApplicationService
    {
        private readonly Queue<GrantRewardResponse> _responses;

        public QueueRewardApplicationService(IReadOnlyList<GrantRewardResponse> responses)
        {
            _responses = new Queue<GrantRewardResponse>(responses);
        }

        public Task<GrantRewardResponse> GrantAsync(GrantRewardCommand command, CancellationToken ct = default)
        {
            if (_responses.Count == 0)
            {
                return Task.FromResult(new GrantRewardResponse
                {
                    Success = true,
                    RewardId = command.RewardId
                });
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class InMemoryRewardIntentRepository : IRewardIntentRepository
    {
        private readonly Dictionary<string, RewardIntentRecord> _intents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _callbackStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _locks = new(StringComparer.Ordinal);

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
            _intents.TryGetValue(rewardIntentId, out var record);
            return Task.FromResult(record);
        }

        public Task<bool> ExpireIfPendingAsync(string rewardIntentId, long nowUtcMs, string errorCode, string errorMessage, CancellationToken ct)
        {
            if (!_intents.TryGetValue(rewardIntentId, out var record))
            {
                return Task.FromResult(false);
            }

            if (!string.Equals(record.Status, RewardIntentStatuses.Pending, StringComparison.Ordinal) || record.ExpiresAtUtc > nowUtcMs)
            {
                return Task.FromResult(false);
            }

            _intents[rewardIntentId] = Clone(record, RewardIntentStatuses.Expired, nowUtcMs, null, errorCode, errorMessage);
            return Task.FromResult(true);
        }

        public Task<RewardIntentConfirmResult> TryBeginCallbackProcessingAsync(
            string provider,
            string rewardIntentId,
            string externalEventId,
            long nowUtcMs,
            string lockToken,
            CancellationToken ct)
        {
            var callbackKey = $"{provider}:{externalEventId}";
            if (_callbackStates.TryGetValue(callbackKey, out var callbackState) &&
                (callbackState == "succeeded" || callbackState == "processing"))
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackDuplicate,
                    Status = RewardIntentStatuses.Failed,
                    ErrorMessage = "duplicate"
                });
            }

            if (!_intents.TryGetValue(rewardIntentId, out var record))
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentNotFound,
                    Status = RewardIntentStatuses.Failed,
                    ErrorMessage = "not found"
                });
            }

            if (record.Status == RewardIntentStatuses.Fulfilled)
            {
                _callbackStates[callbackKey] = "succeeded";
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentAlreadyFulfilled,
                    Status = RewardIntentStatuses.Fulfilled
                });
            }

            if (record.Status == RewardIntentStatuses.Expired)
            {
                _callbackStates[callbackKey] = "succeeded";
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentExpired,
                    Status = RewardIntentStatuses.Expired
                });
            }

            if (record.Status == RewardIntentStatuses.Rejected)
            {
                _callbackStates[callbackKey] = "succeeded";
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackInvalid,
                    Status = RewardIntentStatuses.Rejected
                });
            }

            if (record.Status == RewardIntentStatuses.Failed)
            {
                if (!string.Equals(record.AdNetwork, provider, StringComparison.Ordinal) ||
                    !string.Equals(record.ExternalEventId, externalEventId, StringComparison.Ordinal))
                {
                    return Task.FromResult(new RewardIntentConfirmResult
                    {
                        ResultCode = RewardIntentErrorCodes.CallbackInvalid,
                        Status = RewardIntentStatuses.Failed
                    });
                }
            }
            else if (record.Status != RewardIntentStatuses.Pending)
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackInvalid,
                    Status = record.Status
                });
            }

            if (record.ExpiresAtUtc <= nowUtcMs)
            {
                _intents[rewardIntentId] = Clone(
                    record,
                    RewardIntentStatuses.Expired,
                    nowUtcMs,
                    null,
                    RewardIntentErrorCodes.IntentExpired,
                    "expired");
                _callbackStates[callbackKey] = "succeeded";
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.IntentExpired,
                    Status = RewardIntentStatuses.Expired
                });
            }

            var lockKey = rewardIntentId;
            if (_locks.TryGetValue(lockKey, out var currentLock) && currentLock != lockToken)
            {
                return Task.FromResult(new RewardIntentConfirmResult
                {
                    ResultCode = RewardIntentErrorCodes.CallbackDuplicate,
                    Status = record.Status
                });
            }

            _locks[lockKey] = lockToken;
            _callbackStates[callbackKey] = "processing";
            _intents[rewardIntentId] = Clone(record, record.Status, nowUtcMs, null, null, null, provider, externalEventId);

            return Task.FromResult(new RewardIntentConfirmResult
            {
                ResultCode = "CLAIMED",
                Status = record.Status,
                PlayerId = record.PlayerId,
                RewardId = record.RewardId
            });
        }

        public Task<bool> FinalizeCallbackProcessingAsync(
            string provider,
            string rewardIntentId,
            string externalEventId,
            string lockToken,
            string finalStatus,
            string errorCode,
            string errorMessage,
            long nowUtcMs,
            CancellationToken ct)
        {
            if (!_intents.TryGetValue(rewardIntentId, out var record))
            {
                return Task.FromResult(false);
            }

            if (!_locks.TryGetValue(rewardIntentId, out var currentLock) || currentLock != lockToken)
            {
                return Task.FromResult(false);
            }

            var callbackKey = $"{provider}:{externalEventId}";
            if (string.Equals(finalStatus, RewardIntentStatuses.Fulfilled, StringComparison.Ordinal))
            {
                _intents[rewardIntentId] = Clone(record, RewardIntentStatuses.Fulfilled, nowUtcMs, nowUtcMs, null, null, provider, externalEventId);
                _callbackStates[callbackKey] = "succeeded";
            }
            else if (string.Equals(finalStatus, RewardIntentStatuses.Rejected, StringComparison.Ordinal))
            {
                _intents[rewardIntentId] = Clone(record, RewardIntentStatuses.Rejected, nowUtcMs, null, errorCode, errorMessage, provider, externalEventId);
                _callbackStates[callbackKey] = "succeeded";
            }
            else
            {
                _intents[rewardIntentId] = Clone(record, RewardIntentStatuses.Failed, nowUtcMs, null, errorCode, errorMessage, provider, externalEventId);
                _callbackStates[callbackKey] = "failed";
            }

            _locks.Remove(rewardIntentId);
            return Task.FromResult(true);
        }

        public void SeedIntent(RewardIntentRecord intent)
        {
            _intents[intent.RewardIntentId] = intent;
        }

        private static RewardIntentRecord Clone(
            RewardIntentRecord source,
            string status,
            long updatedAtUtc,
            long? fulfilledAtUtc,
            string? errorCode,
            string? errorMessage,
            string? adNetwork = null,
            string? externalEventId = null)
        {
            return new RewardIntentRecord
            {
                RewardIntentId = source.RewardIntentId,
                PlayerId = source.PlayerId,
                RewardId = source.RewardId,
                Status = status,
                CreatedAtUtc = source.CreatedAtUtc,
                UpdatedAtUtc = updatedAtUtc,
                ExpiresAtUtc = source.ExpiresAtUtc,
                FulfilledAtUtc = fulfilledAtUtc ?? source.FulfilledAtUtc,
                ErrorCode = errorCode ?? source.ErrorCode,
                ErrorMessage = errorMessage ?? source.ErrorMessage,
                AdNetwork = adNetwork ?? source.AdNetwork,
                ExternalEventId = externalEventId ?? source.ExternalEventId
            };
        }
    }
}
