using System.Text.Json.Nodes;
using GameServer.Api.Models.Domain;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Repositories;
using GameServer.Api.Repositories.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class DualWriteSaveRepositoryTests
{
    [Fact]
    public async Task SaveRepository_ReadsOnlyPrimary_AndWritesBoth()
    {
        var primary = new FakeSaveRepository();
        var secondary = new FakeSaveRepository();
        primary.Seed("p1", "primary");
        secondary.Seed("p1", "secondary");
        var repository = new DualWriteSaveRepository(primary, secondary, NullLogger<DualWriteSaveRepository>.Instance);

        var loaded = await repository.LoadAsync("p1", CancellationToken.None);
        await repository.SaveAsync("p2", "payload", CancellationToken.None);

        Assert.Equal("primary", loaded!.Data);
        Assert.Equal("payload", primary.Saves["p2"]);
        Assert.Equal("payload", secondary.Saves["p2"]);
        Assert.Equal(0, secondary.LoadCalls);
    }

    [Fact]
    public async Task SaveRepository_WhenSecondaryFails_DoesNotFailPrimaryWrite()
    {
        var primary = new FakeSaveRepository();
        var secondary = new FakeSaveRepository { ThrowOnSave = true };
        var repository = new DualWriteSaveRepository(primary, secondary, NullLogger<DualWriteSaveRepository>.Instance);

        await repository.SaveAsync("p1", "payload", CancellationToken.None);

        Assert.Equal("payload", primary.Saves["p1"]);
    }

    [Fact]
    public async Task SaveRepository_WhenPrimaryFails_ThrowsAndDoesNotMirror()
    {
        var primary = new FakeSaveRepository { ThrowOnSave = true };
        var secondary = new FakeSaveRepository();
        var repository = new DualWriteSaveRepository(primary, secondary, NullLogger<DualWriteSaveRepository>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync("p1", "payload", CancellationToken.None));

        Assert.Empty(secondary.Saves);
    }

    [Fact]
    public async Task AggregateRepository_SaveRestoresPrimaryLastModified()
    {
        var primary = new FakeAggregateRepository { LastModifiedToAssign = 100 };
        var secondary = new FakeAggregateRepository { LastModifiedToAssign = 200 };
        var repository = new DualWritePlayerAggregateRepository(primary, secondary, NullLogger<DualWritePlayerAggregateRepository>.Instance);
        var aggregate = new PlayerAggregate
        {
            PlayerId = "p1",
            RootDocument = new JsonObject { ["coins"] = 1 },
            LastModified = 0,
            Resources = new Dictionary<string, int>(StringComparer.Ordinal),
            InventoryItems = new Dictionary<string, int>(StringComparer.Ordinal)
        };

        await repository.SaveAsync(aggregate, CancellationToken.None);

        Assert.Equal(100, aggregate.LastModified);
        Assert.Equal(100, secondary.LastSaveInputLastModified);
        Assert.NotSame(aggregate, secondary.LastSaved);
    }

    [Fact]
    public async Task ResourcesRepository_ReadsPrimary_AndSecondaryFailureIsBestEffort()
    {
        var primary = new FakeResourcesRepository();
        var secondary = new FakeResourcesRepository { ThrowOnSave = true };
        primary.Record = new PlayerResourcesRecord
        {
            PlayerId = "p1",
            Resources = new Dictionary<string, int>(StringComparer.Ordinal) { ["Gold"] = 5 },
            Version = 3
        };
        var repository = new DualWritePlayerResourcesRepository(primary, secondary, NullLogger<DualWritePlayerResourcesRepository>.Instance);

        var loaded = await repository.GetAsync("p1", CancellationToken.None);
        await repository.SaveAsync(primary.Record, CancellationToken.None);

        Assert.Equal(5, loaded!.Resources["Gold"]);
        Assert.Equal(1, primary.SaveCalls);
        Assert.Equal(1, secondary.SaveCalls);
    }

    [Fact]
    public async Task InventoryRepository_ReadsPrimary_AndSecondaryFailureIsBestEffort()
    {
        var primary = new FakeInventoryRepository();
        var secondary = new FakeInventoryRepository { ThrowOnSave = true };
        primary.Record = new PlayerInventoryRecord
        {
            PlayerId = "p1",
            Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["book"] = 2 },
            Version = 4
        };
        var repository = new DualWritePlayerInventoryRepository(primary, secondary, NullLogger<DualWritePlayerInventoryRepository>.Instance);

        var loaded = await repository.GetAsync("p1", CancellationToken.None);
        await repository.SaveAsync(primary.Record, CancellationToken.None);

        Assert.Equal(2, loaded!.Items["book"]);
        Assert.Equal(1, primary.SaveCalls);
        Assert.Equal(1, secondary.SaveCalls);
    }

    private sealed class FakeSaveRepository : ISaveRepository
    {
        public Dictionary<string, string> Saves { get; } = new(StringComparer.Ordinal);
        public bool ThrowOnSave { get; init; }
        public int LoadCalls { get; private set; }

        public void Seed(string playerId, string data) => Saves[playerId] = data;

        public Task SaveAsync(string playerId, string data, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Saves[playerId] = data;
            return Task.CompletedTask;
        }

        public Task<SaveRecord?> LoadAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCalls++;
            return Task.FromResult(Saves.TryGetValue(playerId, out var data)
                ? new SaveRecord { Data = data, LastModified = 1 }
                : null);
        }

        public Task<long?> GetLastModifiedTimestampAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<long?>(Saves.ContainsKey(playerId) ? 1 : null);
        }

        public Task DeleteAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saves.Remove(playerId);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string playerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Saves.ContainsKey(playerId));
        }
    }

    private sealed class FakeAggregateRepository : IPlayerAggregateRepository
    {
        public long LastModifiedToAssign { get; init; }
        public bool ThrowOnSave { get; init; }
        public PlayerAggregate? LastSaved { get; private set; }
        public long LastSaveInputLastModified { get; private set; }

        public Task<PlayerAggregate> LoadAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PlayerAggregate
            {
                PlayerId = playerId,
                RootDocument = new JsonObject(),
                LastModified = LastModifiedToAssign,
                Resources = new Dictionary<string, int>(StringComparer.Ordinal),
                InventoryItems = new Dictionary<string, int>(StringComparer.Ordinal)
            });
        }

        public Task SaveAsync(PlayerAggregate player, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            LastSaveInputLastModified = player.LastModified;
            LastSaved = player;
            player.LastModified = LastModifiedToAssign;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeResourcesRepository : IPlayerResourcesRepository
    {
        public PlayerResourcesRecord? Record { get; set; }
        public bool ThrowOnSave { get; init; }
        public int SaveCalls { get; private set; }

        public Task<PlayerResourcesRecord?> GetAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Record);
        }

        public Task SaveAsync(PlayerResourcesRecord record, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Record = record;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryRepository : IPlayerInventoryRepository
    {
        public PlayerInventoryRecord? Record { get; set; }
        public bool ThrowOnSave { get; init; }
        public int SaveCalls { get; private set; }

        public Task<PlayerInventoryRecord?> GetAsync(string playerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Record);
        }

        public Task SaveAsync(PlayerInventoryRecord record, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Record = record;
            return Task.CompletedTask;
        }
    }
}
