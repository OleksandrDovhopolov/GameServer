using System.Text.Json;
using GameServer.Api.Models.Domain;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Services;

public sealed class WheelRewardCatalog : IWheelRewardCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<WheelReward> _rewards;
    private readonly int _totalWeight;
    private readonly IRandomGenerator _randomGenerator;

    public WheelRewardCatalog(
        IWebHostEnvironment environment,
        IRandomGenerator randomGenerator,
        ILogger<WheelRewardCatalog> logger)
    {
        _randomGenerator = randomGenerator;

        var filePath = Path.Combine(environment.ContentRootPath, "Data", "rewards.json");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Wheel rewards configuration file was not found.", filePath);
        }

        using var stream = File.OpenRead(filePath);
        var config = JsonSerializer.Deserialize<WheelRewardsConfig>(stream, JsonOptions);
        var rewards = config?.Rewards ?? new List<WheelReward>();
        _totalWeight = WeightedRewardSelector.ValidateAndGetTotalWeight(rewards);
        _rewards = rewards;

        logger.LogInformation("Loaded {RewardCount} wheel rewards with total weight {TotalWeight}.", _rewards.Count, _totalWeight);
    }

    public IReadOnlyList<WheelReward> GetRewards() => _rewards;

    public string RollRewardId()
    {
        var roll = _randomGenerator.Next(_totalWeight);
        return WeightedRewardSelector.SelectRewardId(_rewards, _totalWeight, roll);
    }

    private sealed class WheelRewardsConfig
    {
        public List<WheelReward> Rewards { get; init; } = new();
    }
}
