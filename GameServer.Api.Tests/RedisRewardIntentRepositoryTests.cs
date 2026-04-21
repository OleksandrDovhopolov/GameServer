using GameServer.Api.Repositories;
using GameServer.Api.Services;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RedisRewardIntentRepositoryTests
{
    [Theory]
    [InlineData("CLAIMED", "pending", "player_1", "Gems", null, "CLAIMED", "pending", "player_1", "Gems")]
    [InlineData("CALLBACK_DUPLICATE", "", "", "", "duplicate", "CALLBACK_DUPLICATE", "failed", null, null)]
    [InlineData(null, null, null, null, null, "REWARD_GRANT_FAILED", "failed", null, null)]
    public void ParseBeginResult_MapsBranchValues(
        string? resultCode,
        string? status,
        string? playerId,
        string? rewardId,
        string? errorMessage,
        string expectedCode,
        string expectedStatus,
        string? expectedPlayerId,
        string? expectedRewardId)
    {
        var result = RedisRewardIntentRepository.ParseBeginResult(resultCode, status, playerId, rewardId, errorMessage);

        Assert.Equal(expectedCode, result.ResultCode);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedPlayerId, result.PlayerId);
        Assert.Equal(expectedRewardId, result.RewardId);
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            Assert.Null(result.ErrorMessage);
        }
        else
        {
            Assert.Equal(errorMessage, result.ErrorMessage);
        }
    }
}
