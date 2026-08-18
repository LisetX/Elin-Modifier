internal static class GuaranteedGatheringRewardsPolicy
{
    internal const float GuaranteedDismantleRoll = 0f;

    internal static float ResolveDismantleRoll(float originalRoll, bool enabled)
    {
        return enabled ? GuaranteedDismantleRoll : originalRoll;
    }

    internal static float ResolveDismantleCoefficient(float originalCoefficient, bool enabled)
    {
        if (!enabled)
            return originalCoefficient;

        return Math.Max(1f, (float)Math.Truncate(originalCoefficient));
    }

    internal static int CalculateFullRefundCount(int required, int itemCount, int craftResultCount)
    {
        if (required <= 0 || itemCount <= 0)
            return 0;

        var divisor = Math.Max(1, craftResultCount);
        var total = (long)required * itemCount;
        var roundedUp = (total + divisor - 1L) / divisor;
        return (int)Math.Min(int.MaxValue, roundedUp);
    }

    internal static bool ShouldUseFullRecipeRefund(
        bool enabled,
        bool useVanillaDismantleMechanism,
        bool recipeExists,
        bool hasRequiredIngredients,
        bool preservesOriginalRestriction)
    {
        return enabled &&
               !useVanillaDismantleMechanism &&
               recipeExists &&
               hasRequiredIngredients &&
               preservesOriginalRestriction;
    }

    internal static bool IsLearnableRecipeSource(
        bool sourceExists,
        bool needsFactory,
        bool isQuickCraft)
    {
        return sourceExists && (needsFactory || isQuickCraft);
    }

    internal static bool IsMatchingDismantleRecipeAttempt(
        bool enabled,
        string? pendingRecipeId,
        string? requestedRecipeId,
        int pendingFrame,
        int currentFrame)
    {
        return enabled &&
               pendingFrame == currentFrame &&
               !string.IsNullOrWhiteSpace(pendingRecipeId) &&
               string.Equals(pendingRecipeId, requestedRecipeId, StringComparison.Ordinal);
    }
}
