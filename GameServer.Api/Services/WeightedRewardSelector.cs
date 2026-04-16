using GameServer.Api.Models.Domain;

namespace GameServer.Api.Services;

public static class WeightedRewardSelector
{
    public static int ValidateAndGetTotalWeight(IReadOnlyCollection<WheelReward> rewards)
    {
        if (rewards.Count == 0)
        {
            throw new InvalidOperationException("Wheel rewards configuration must contain at least one reward.");
        }

        var totalWeight = 0L;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reward in rewards)
        {
            if (string.IsNullOrWhiteSpace(reward.Id))
            {
                throw new InvalidOperationException("Wheel reward id cannot be empty.");
            }

            if (!seenIds.Add(reward.Id))
            {
                throw new InvalidOperationException($"Wheel reward id '{reward.Id}' must be unique.");
            }

            if (reward.Weight <= 0)
            {
                throw new InvalidOperationException($"Wheel reward '{reward.Id}' must have weight greater than 0.");
            }

            totalWeight += reward.Weight;
        }

        if (totalWeight > int.MaxValue)
        {
            throw new InvalidOperationException("Wheel rewards total weight exceeds supported maximum.");
        }

        return (int)totalWeight;
    }

    public static string SelectRewardId(IReadOnlyList<WheelReward> rewards, int totalWeight, int rollValue)
    {
        if (rollValue < 0 || rollValue >= totalWeight)
        {
            throw new ArgumentOutOfRangeException(nameof(rollValue), "Roll value must be within total weight range.");
        }

        var cumulative = 0;
        foreach (var reward in rewards)
        {
            cumulative += reward.Weight;
            if (rollValue < cumulative)
            {
                return reward.Id;
            }
        }

        return rewards[^1].Id;
    }
}
