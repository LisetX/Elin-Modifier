using System;

internal readonly struct NpcTemplateValueBounds
{
    internal NpcTemplateValueBounds(long fixedValue, long minimum, long maximum)
    {
        FixedValue = fixedValue;
        Minimum = minimum;
        Maximum = maximum;
    }

    internal long FixedValue { get; }
    internal long Minimum { get; }
    internal long Maximum { get; }
}

internal static class NpcTemplateValueMath
{
    internal static NpcTemplateValueBounds GetCharaSourceBounds(
        int sourceValue,
        int level,
        int levelFactor)
    {
        level = Math.Min(10000000, Math.Max(1, level));
        var firstMaximum = Math.Max(0, level / 2);
        var secondMaximum = Math.Max(0, level / 3 - 1);
        var fixedValue = CalculateCharaSourceValue(sourceValue, level, levelFactor, 0, 0);
        var minimum = long.MaxValue;
        var maximum = long.MinValue;
        var firstValues = firstMaximum == 0 ? new[] { 0 } : new[] { 0, firstMaximum };
        var secondValues = secondMaximum == 0 ? new[] { 0 } : new[] { 0, secondMaximum };
        for (var firstIndex = 0; firstIndex < firstValues.Length; firstIndex++)
        {
            for (var secondIndex = 0; secondIndex < secondValues.Length; secondIndex++)
            {
                var value = CalculateCharaSourceValue(
                    sourceValue,
                    level,
                    levelFactor,
                    firstValues[firstIndex],
                    secondValues[secondIndex]);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        return new NpcTemplateValueBounds(fixedValue, minimum, maximum);
    }

    internal static long CalculateCharaSourceValue(
        int sourceValue,
        int level,
        int levelFactor,
        int firstRandom,
        int secondRandom)
    {
        level = Math.Min(10000000, Math.Max(1, level));
        var levelTerm = (long)level - 1L + firstRandom;
        var factor = 100L + levelTerm * levelFactor / 10L;
        var value = (long)sourceValue * factor / 100L +
                    (long)secondRandom * levelFactor / 100L;
        return Math.Min(99999999L, value);
    }
}
