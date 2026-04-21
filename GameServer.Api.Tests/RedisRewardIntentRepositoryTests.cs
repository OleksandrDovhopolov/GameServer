using GameServer.Api.Repositories;
using GameServer.Api.Services;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RedisRewardIntentRepositoryTests
{
    [Theory]
    [InlineData("OK", "fulfilled", "2050", null, "OK", "fulfilled", 2050)]
    [InlineData("CALLBACK_DUPLICATE", "", "", "duplicate", "CALLBACK_DUPLICATE", "failed", null)]
    [InlineData(null, null, null, null, "REWARD_GRANT_FAILED", "failed", null)]
    public void ParseConfirmResult_MapsBranchValues(
        string? resultCode,
        string? status,
        string? balanceRaw,
        string? errorMessage,
        string expectedCode,
        string expectedStatus,
        int? expectedBalance)
    {
        var result = RedisRewardIntentRepository.ParseConfirmResult(resultCode, status, balanceRaw, errorMessage);

        Assert.Equal(expectedCode, result.ResultCode);
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedBalance, result.NewCrystalsBalance);
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
