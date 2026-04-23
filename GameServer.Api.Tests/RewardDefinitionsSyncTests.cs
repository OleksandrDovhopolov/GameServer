using System.Text.Json;
using Xunit;

namespace GameServer.Api.Tests;

public sealed class RewardDefinitionsSyncTests
{
    [Fact]
    public void RewardDefinitions_ShouldContainExpectedRewardIds()
    {
        var definitions = LoadRewardDefinitions();
        var expectedRewardIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Energy",
            "Gold",
            "Gems",
            "Spring_Collection",
            "Winter_Collection",
            "SmallPack",
            "MiddlePack",
            "BigPack",
            "Lazurite_Pack",
            "Bronze_Pack",
            "Emerald_Pack",
            "Sapphire_Pack",
            "Ruby_Pack",
            "fortune_wheel_ad_spin"
        };

        var actualRewardIds = definitions.Rewards
            .Select(x => x.RewardId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expectedRewardIds.Count, definitions.Rewards.Count);
        Assert.Equal(expectedRewardIds, actualRewardIds);
    }

    [Fact]
    public void RewardDefinitions_ShouldAssignSourceByWheelMembership()
    {
        var definitions = LoadRewardDefinitions();
        var wheelRewardIds = LoadWheelRewardIds();

        foreach (var definition in definitions.Rewards)
        {
            var expectedSource = wheelRewardIds.Contains(definition.RewardId) ? "wheel" : "grant";
            Assert.Equal(expectedSource, definition.Source);
        }
    }

    [Fact]
    public void RewardDefinitions_ShouldNotContainUiOnlyFields()
    {
        var rawJson = File.ReadAllText(GetRepoPath("GameServer.Api", "Data", "reward_definitions.json"));

        Assert.DoesNotContain("TotalAmountForUi", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Icon\"", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RewardDefinitions_ShouldContainExpectedMultipartRewards()
    {
        var definitions = LoadRewardDefinitions();
        var byId = definitions.Rewards.ToDictionary(x => x.RewardId, StringComparer.Ordinal);

        Assert.Equal(3, byId["Spring_Collection"].Parts.Count);
        Assert.Equal(5, byId["BigPack"].Parts.Count);
    }

    [Fact]
    public void RewardDefinitions_ShouldContainOnlyValidParts()
    {
        var definitions = LoadRewardDefinitions();
        foreach (var definition in definitions.Rewards)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.RewardId));
            Assert.False(string.IsNullOrWhiteSpace(definition.Source));
            Assert.NotEmpty(definition.Parts);

            foreach (var part in definition.Parts)
            {
                Assert.True(part.Kind > 0);
                Assert.False(string.IsNullOrWhiteSpace(part.TargetId));
                Assert.True(part.Amount > 0);
            }
        }
    }

    private static RewardDefinitionsConfig LoadRewardDefinitions()
    {
        var path = GetRepoPath("GameServer.Api", "Data", "reward_definitions.json");
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<RewardDefinitionsConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        Assert.NotNull(config!.Rewards);
        return config!;
    }

    private static HashSet<string> LoadWheelRewardIds()
    {
        var path = GetRepoPath("GameServer.Api", "Data", "rewards.json");
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<WheelRewardsConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(config);
        return config!.Rewards
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameServer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test base directory.");
        }

        return Path.Combine([directory.FullName, ..segments]);
    }

    private sealed class RewardDefinitionsConfig
    {
        public List<RewardDefinitionDto> Rewards { get; init; } = new();
    }

    private sealed class RewardDefinitionDto
    {
        public string RewardId { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public List<RewardPartDto> Parts { get; init; } = new();
    }

    private sealed class RewardPartDto
    {
        public int Kind { get; init; }
        public string TargetId { get; init; } = string.Empty;
        public int Amount { get; init; }
        public string? Category { get; init; }
    }

    private sealed class WheelRewardsConfig
    {
        public List<WheelRewardDto> Rewards { get; init; } = new();
    }

    private sealed class WheelRewardDto
    {
        public string Id { get; init; } = string.Empty;
    }
}
