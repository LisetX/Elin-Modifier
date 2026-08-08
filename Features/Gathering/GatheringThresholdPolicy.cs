using System;

internal static class GatheringThresholdPolicy
{
    internal static int CalculateRequiredHardness(int baseHardness, int hpPercent, bool isHardMaterial)
    {
        var value = (long)Math.Max(0, baseHardness) * Math.Max(0, hpPercent) / 100L;
        if (isHardMaterial)
            value *= 3L;
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    internal static int NormalizeRequiredSkillLevel(int value)
    {
        return Math.Max(0, value);
    }
}
