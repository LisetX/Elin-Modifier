using System;
using System.Collections.Generic;

internal readonly struct NpcInfoLootEncoding
{
    internal NpcInfoLootEncoding(double probability, int minimumQuantity, int maximumQuantity, double expectedQuantity)
    {
        Probability = probability;
        MinimumQuantity = minimumQuantity;
        MaximumQuantity = maximumQuantity;
        ExpectedQuantity = expectedQuantity;
    }

    internal double Probability { get; }
    internal int MinimumQuantity { get; }
    internal int MaximumQuantity { get; }
    internal double ExpectedQuantity { get; }
}

internal static class NpcInfoProbabilityMath
{
    private static readonly Dictionary<int, IReadOnlyDictionary<int, double>> OffsetCache =
        new Dictionary<int, IReadOnlyDictionary<int, double>>();

    internal static IReadOnlyDictionary<int, double> GetDefaultSpawnLevelOffsets(int dangerLevel)
    {
        var normalizedDangerLevel = Math.Max(1, dangerLevel);
        var firstRange = Math.Min(normalizedDangerLevel * 2, 20);
        if (OffsetCache.TryGetValue(firstRange, out var cached))
            return cached;

        var result = new Dictionary<int, double>();
        for (var first = 0; first < firstRange; first++)
        {
            var firstProbability = 1d / firstRange;
            for (var second = 0; second <= first; second++)
            {
                var secondProbability = firstProbability / (first + 1d);
                for (var third = 0; third <= second; third++)
                {
                    var thirdProbability = secondProbability / (second + 1d);
                    for (var fourth = 0; fourth <= third; fourth++)
                    {
                        var probability = thirdProbability / (third + 1d);
                        result.TryGetValue(fourth, out var current);
                        result[fourth] = current + probability;
                    }
                }
            }
        }

        OffsetCache[firstRange] = result;
        return result;
    }

    internal static bool IsDefaultSpawnCandidate(bool blocked, bool hasDoor, bool pcSync)
    {
        return !blocked && !hasDoor && !pcSync;
    }

    internal static NpcInfoLootEncoding DecodeLootValue(int encodedValue)
    {
        if (encodedValue <= 0)
            return new NpcInfoLootEncoding(0d, 0, 0, 0d);

        if (encodedValue < 1000)
        {
            var probability = encodedValue / 1000d;
            return new NpcInfoLootEncoding(probability, 1, 1, probability);
        }

        var minimum = encodedValue / 1000;
        var remainder = encodedValue % 1000;
        var maximum = minimum + (remainder > 0 ? 1 : 0);
        return new NpcInfoLootEncoding(1d, minimum, maximum, minimum + remainder / 1000d);
    }

}
