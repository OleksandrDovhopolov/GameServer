using System.Reflection;
using StackExchange.Redis;
using GameServer.Api.Models.Domain.BattlePass;
using GameServer.Api.Repositories;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RedisBattlePassSeasonRepositoryTests
{
    [Fact]
    public void DeserializeSeason_MapsLegacyGoldProductIdToPremiumProductId()
    {
        const string payload = """
        {
          "Id": "Test_Battle_Pass",
          "Title": "Test Battle Pass",
          "StartAtUtcMs": 1,
          "EndAtUtcMs": 2,
          "MaxLevel": 3,
          "Status": "active",
          "ConfigVersion": "v1",
          "GoldProductId": "battle_pass_premium_test_battle_pass",
          "PlatinumProductId": "battle_pass_platinum_test_battle_pass",
          "CreatedAtUtcMs": 1,
          "UpdatedAtUtcMs": 2
        }
        """;

        var deserializeSeason = typeof(RedisBattlePassSeasonRepository)
            .GetMethod("DeserializeSeason", BindingFlags.NonPublic | BindingFlags.Static);

        var season = Assert.IsType<BattlePassSeasonRecord>(deserializeSeason!.Invoke(null, [new RedisValue(payload)]));

        Assert.Equal("battle_pass_premium_test_battle_pass", season.PremiumProductId);
        Assert.Equal("battle_pass_platinum_test_battle_pass", season.PlatinumProductId);
    }
}
