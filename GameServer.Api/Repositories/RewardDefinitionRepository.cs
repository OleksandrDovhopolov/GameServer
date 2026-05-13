using System.Text.Json;
using GameServer.Api.Models.Domain.Rewards;
using GameServer.Api.Services.Interfaces;

namespace GameServer.Api.Repositories;

public sealed class RewardDefinitionRepository : IRewardDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, RewardDefinition> _definitionsById;

    public RewardDefinitionRepository(
        IWebHostEnvironment environment,
        ILogger<RewardDefinitionRepository> logger)
    {
        var definitionsPath = Path.Combine(environment.ContentRootPath, "Data", "reward_definitions.json");
        if (!File.Exists(definitionsPath))
        {
            throw new FileNotFoundException("Reward definitions configuration file was not found.", definitionsPath);
        }

        //TODO remove it from here. move to WheelRewardCatalog or create new place
        var wheelRewardsPath = Path.Combine(environment.ContentRootPath, "Data", "rewards.json");
        if (!File.Exists(wheelRewardsPath))
        {
            throw new FileNotFoundException("Wheel rewards configuration file was not found.", wheelRewardsPath);
        }

        using var definitionsStream = File.OpenRead(definitionsPath);
        var definitionsConfig = JsonSerializer.Deserialize<RewardDefinitionsConfig>(definitionsStream, JsonOptions);
        var definitions = definitionsConfig?.Rewards ?? [];

        _definitionsById = BuildDefinitionMap(definitions);

        using var wheelRewardsStream = File.OpenRead(wheelRewardsPath);
        var wheelRewardsConfig = JsonSerializer.Deserialize<WheelRewardsConfig>(wheelRewardsStream, JsonOptions);
        var wheelRewardIds = (wheelRewardsConfig?.Rewards ?? [])
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missingWheelDefinitions = wheelRewardIds
            .Where(wheelRewardId => !_definitionsById.ContainsKey(wheelRewardId))
            .ToList();
        if (missingWheelDefinitions.Count > 0)
        {
            throw new InvalidOperationException(
                $"Reward definitions are missing wheel rewards: {string.Join(", ", missingWheelDefinitions)}");
        }

        logger.LogInformation("Loaded {DefinitionCount} reward definitions.", _definitionsById.Count);
    }

    public Task<RewardDefinition?> GetByIdAsync(string rewardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_definitionsById.TryGetValue(rewardId, out var definition) ? definition : null);
    }

    private static IReadOnlyDictionary<string, RewardDefinition> BuildDefinitionMap(List<RewardDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            throw new InvalidOperationException("Reward definitions configuration must contain at least one reward.");
        }

        var map = new Dictionary<string, RewardDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            ValidateDefinition(definition);
            if (!map.TryAdd(definition.RewardId, definition))
            {
                throw new InvalidOperationException($"Reward definition id '{definition.RewardId}' must be unique.");
            }
        }

        return map;
    }

    private static void ValidateDefinition(RewardDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.RewardId))
        {
            throw new InvalidOperationException("Reward definition id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(definition.Source))
        {
            throw new InvalidOperationException($"Reward definition '{definition.RewardId}' source cannot be empty.");
        }

        if (definition.Parts.Count == 0)
        {
            throw new InvalidOperationException($"Reward definition '{definition.RewardId}' must contain at least one reward part.");
        }

        foreach (var part in definition.Parts)
        {
            if (part.Kind == RewardKind.Unknown)
            {
                throw new InvalidOperationException($"Reward definition '{definition.RewardId}' contains unknown reward part kind.");
            }

            if (string.IsNullOrWhiteSpace(part.TargetId))
            {
                throw new InvalidOperationException($"Reward definition '{definition.RewardId}' contains empty reward target id.");
            }

            if (part.Kind == RewardKind.FreeSpin && part.Amount != 1)
            {
                throw new InvalidOperationException(
                    $"Reward definition '{definition.RewardId}' free spin part must have amount equal to one.");
            }

            if (part.Kind != RewardKind.FreeSpin && part.Amount <= 0)
            {
                throw new InvalidOperationException($"Reward definition '{definition.RewardId}' contains non-positive reward amount.");
            }
        }
    }

    private sealed class RewardDefinitionsConfig
    {
        public List<RewardDefinition> Rewards { get; init; } = new();
    }

    private sealed class WheelRewardsConfig
    {
        public List<WheelRewardConfig> Rewards { get; init; } = new();
    }

    private sealed class WheelRewardConfig
    {
        public string Id { get; init; } = string.Empty;
    }
}
