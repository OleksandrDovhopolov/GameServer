using GameServer.Api.Repositories;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class PlayerSaveRedisKeysTests
{
    [Theory]
    [InlineData("save:p1:p1")]
    [InlineData("save:p1:meta")]
    [InlineData("save:p1:resources")]
    [InlineData("save:p1:resources:version")]
    [InlineData("save:p1:inventory")]
    [InlineData("save:p1:inventory:version")]
    public void TryGetPlayerId_WhenSaveKey_ReturnsPlayerId(string key)
    {
        var parsed = PlayerSaveRedisKeys.TryGetPlayerId(key, out var playerId);

        Assert.True(parsed);
        Assert.Equal("p1", playerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("other:p1:resources")]
    [InlineData("save:")]
    [InlineData("save::resources")]
    [InlineData("save:p1")]
    public void TryGetPlayerId_WhenMalformed_ReturnsFalse(string key)
    {
        var parsed = PlayerSaveRedisKeys.TryGetPlayerId(key, out var playerId);

        Assert.False(parsed);
        Assert.Equal(string.Empty, playerId);
    }

    [Fact]
    public void KeyBuilders_UseCurrentRedisContract()
    {
        Assert.Equal("save:p1:p1", PlayerSaveRedisKeys.Save("p1").ToString());
        Assert.Equal("save:p1:meta", PlayerSaveRedisKeys.Meta("p1").ToString());
        Assert.Equal("save:p1:resources", PlayerSaveRedisKeys.Resources("p1").ToString());
        Assert.Equal("save:p1:resources:version", PlayerSaveRedisKeys.ResourcesVersion("p1").ToString());
        Assert.Equal("save:p1:inventory", PlayerSaveRedisKeys.Inventory("p1").ToString());
        Assert.Equal("save:p1:inventory:version", PlayerSaveRedisKeys.InventoryVersion("p1").ToString());
    }
}
