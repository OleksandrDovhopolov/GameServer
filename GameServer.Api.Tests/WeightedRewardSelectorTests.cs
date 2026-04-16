using GameServer.Api.Models.Domain;
using GameServer.Api.Services;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class WeightedRewardSelectorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ValidateAndGetTotalWeight_RejectsNonPositiveWeights(int badWeight)
    {
        var rewards = new List<WheelReward>
        {
            new() { Id = "gold_500", Weight = badWeight }
        };

        Assert.Throws<InvalidOperationException>(() => WeightedRewardSelector.ValidateAndGetTotalWeight(rewards));
    }

    [Fact]
    public void SelectRewardId_ReturnsOnlyConfiguredRewardIdsAcrossEntireRange()
    {
        var rewards = new List<WheelReward>
        {
            new() { Id = "gold_500", Weight = 40 },
            new() { Id = "gems_10", Weight = 10 },
            new() { Id = "energy_refill", Weight = 30 },
            new() { Id = "legendary_chest", Weight = 2 }
        };

        var totalWeight = WeightedRewardSelector.ValidateAndGetTotalWeight(rewards);
        var validIds = rewards.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        for (var roll = 0; roll < totalWeight; roll++)
        {
            var selectedRewardId = WeightedRewardSelector.SelectRewardId(rewards, totalWeight, roll);
            Assert.Contains(selectedRewardId, validIds);
        }
    }
}
