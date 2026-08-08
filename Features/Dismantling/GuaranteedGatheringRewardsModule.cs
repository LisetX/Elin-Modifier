using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

internal sealed class GuaranteedGatheringRewardsModule
{
    internal bool DismantleAlwaysReturnsMaterials { get; private set; }
    internal bool DismantlingAlwaysLearnsRecipe { get; private set; }

    internal void Load(bool dismantleAlwaysReturnsMaterials, bool dismantlingAlwaysLearnsRecipe)
    {
        DismantleAlwaysReturnsMaterials = dismantleAlwaysReturnsMaterials;
        DismantlingAlwaysLearnsRecipe = dismantlingAlwaysLearnsRecipe;
    }

    internal void Reset()
    {
        DismantleAlwaysReturnsMaterials = false;
        DismantlingAlwaysLearnsRecipe = false;
    }

    internal bool SetDismantleAlwaysReturnsMaterials(bool enabled)
    {
        if (DismantleAlwaysReturnsMaterials == enabled)
            return false;
        DismantleAlwaysReturnsMaterials = enabled;
        return true;
    }

    internal bool SetDismantlingAlwaysLearnsRecipe(bool enabled)
    {
        if (DismantlingAlwaysLearnsRecipe == enabled)
            return false;
        DismantlingAlwaysLearnsRecipe = enabled;
        return true;
    }
}

internal static class GuaranteedGatheringRewardsPatchContext
{
    [ThreadStatic]
    private static bool _forceDismantleMaterialRoll;

    [ThreadStatic]
    private static bool _suppressOriginalDismantledOutput;

    [ThreadStatic]
    private static string? _pendingDismantledRecipeId;

    [ThreadStatic]
    private static int _pendingDismantledRecipeFrame;

    [ThreadStatic]
    private static bool _forceRecipeDiscoveryRoll;

    [ThreadStatic]
    private static bool _recipeDiscoveryRollConsumed;

    private static readonly MethodInfo? OriginalDismantledOutputGuardTarget =
        AccessTools.DeclaredMethod(
            typeof(TaskHarvest),
            "ShouldGenerateDismantled",
            new[] { typeof(string) });

    private sealed class DismantleRefundState
    {
        internal bool ScopeActive;
        internal bool FullRecipeRefund;
        internal int ItemCount;
        internal int CraftResultCount;
        internal int ItemLevel;
        internal int Decay;
        internal Point? DropPoint;
        internal SourceMaterial.Row? Material;
        internal List<Recipe.Ingredient>? Ingredients;
    }

    private static GuaranteedGatheringRewardsModule? Current =>
        ElinModifierPlugin.ActiveModules?.GuaranteedGatheringRewards;

    private static bool IsDismantleMaterialReturnEnabled =>
        Current?.DismantleAlwaysReturnsMaterials == true;

    private static bool IsDismantlingRecipeEnabled =>
        Current?.DismantlingAlwaysLearnsRecipe == true;

    [HarmonyPatch]
    private static class TaskHarvestMaterialRollScopePatch
    {
        private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(
            typeof(TaskHarvest),
            "HarvestThing",
            Type.EmptyTypes);

        [HarmonyPrepare]
        private static bool Prepare() => Target != null;

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => Target;

        private static void Prefix(TaskHarvest __instance, out DismantleRefundState? __state)
        {
            __state = null;
            _pendingDismantledRecipeId = null;
            _pendingDismantledRecipeFrame = -1;

            try
            {
                if (__instance.harvestType != BaseTaskHarvest.HarvestType.Disassemble)
                    return;

                if (IsDismantlingRecipeEnabled && __instance.owner?.IsPC == true)
                {
                    var recipeId = __instance.target?.source?.RecipeID;
                    if (!string.IsNullOrWhiteSpace(recipeId))
                    {
                        _pendingDismantledRecipeId = recipeId;
                        _pendingDismantledRecipeFrame = UnityEngine.Time.frameCount;
                    }
                }

                if (!IsDismantleMaterialReturnEnabled)
                    return;

                var state = new DismantleRefundState { ScopeActive = true };
                __state = state;
                _forceDismantleMaterialRoll = true;

                if (!TryPrepareFullRecipeRefund(__instance, state))
                    return;

                state.FullRecipeRefund = true;
                _suppressOriginalDismantledOutput = true;
            }
            catch
            {
                _suppressOriginalDismantledOutput = false;
            }
        }

        private static void Postfix(DismantleRefundState? __state)
        {
            try
            {
                if (__state?.FullRecipeRefund == true)
                    DropFullRecipeRefund(__state);
            }
            finally
            {
                if (__state?.ScopeActive == true)
                {
                    _suppressOriginalDismantledOutput = false;
                    _forceDismantleMaterialRoll = false;
                }
            }
        }

        private static Exception? Finalizer(Exception? __exception, DismantleRefundState? __state)
        {
            if (__state?.ScopeActive == true)
            {
                _suppressOriginalDismantledOutput = false;
                _forceDismantleMaterialRoll = false;
            }
            return __exception;
        }

        private static bool TryPrepareFullRecipeRefund(
            TaskHarvest task,
            DismantleRefundState state)
        {
            if (OriginalDismantledOutputGuardTarget == null)
                return false;

            var target = task.target;
            if (target == null || target.isDestroyed)
                return false;

            var preservesOriginalRestriction =
                target.trait is not TraitFakeTile &&
                target.trait is not TraitGrave &&
                target.trait is not TraitFoodFishSlice &&
                !target.isCopy &&
                !target.HasElement(764, false) &&
                !(GameAccess.World.CurrentZone?.IsUserZone == true && target.isNPCProperty);

            var source = RecipeManager.Get(target.id);
            var recipeExists = source != null &&
                               GuaranteedGatheringRewardsPolicy.IsLearnableRecipeSource(
                                   true,
                                   source.NeedFactory,
                                   source.IsQuickCraft);
            if (!recipeExists || source == null)
                return false;

            var ingredients = source.GetIngredients();
            var requiredIngredients = new List<Recipe.Ingredient>();
            if (ingredients != null)
            {
                foreach (var ingredient in ingredients)
                {
                    if (ingredient == null || ingredient.optional || ingredient.req <= 0)
                        continue;
                    requiredIngredients.Add(ingredient);
                }
            }

            if (!GuaranteedGatheringRewardsPolicy.ShouldUseFullRecipeRefund(
                    true,
                    recipeExists,
                    requiredIngredients.Count > 0,
                    preservesOriginalRestriction))
                return false;

            var point = task.pos.IsBlocked ? task.owner.pos : task.pos;
            state.ItemCount = Math.Max(1, target.Num);
            state.CraftResultCount = Math.Max(1, target.trait.CraftNum);
            state.ItemLevel = Math.Max(1, target.LV * 2 / 3);
            state.Decay = target.decay;
            state.DropPoint = new Point(point);
            state.Material = target.material;
            state.Ingredients = requiredIngredients;
            return true;
        }

        private static void DropFullRecipeRefund(DismantleRefundState state)
        {
            if (state.DropPoint == null || state.Ingredients == null || GameAccess.World.CurrentMap == null)
                return;

            foreach (var ingredient in state.Ingredients)
            {
                Thing? thing = null;
                try
                {
                    var count = GuaranteedGatheringRewardsPolicy.CalculateFullRefundCount(
                        ingredient.req,
                        state.ItemCount,
                        state.CraftResultCount);
                    if (count <= 0)
                        continue;

                    thing = CreateRefundIngredient(ingredient, state.Material, state.ItemLevel);
                    if (thing == null)
                        continue;

                    thing.SetNum(count);
                    thing.decay = state.Decay;
                    GameAccess.World.CurrentMap.TrySmoothPick(state.DropPoint, thing, GameAccess.Characters.PlayerCharacter);
                    thing = null;
                }
                catch
                {
                    try { thing?.Destroy(); } catch { }
                }
            }
        }

        private static Thing? CreateRefundIngredient(
            Recipe.Ingredient ingredient,
            SourceMaterial.Row? material,
            int level)
        {
            if (ingredient.useCat)
            {
                var materialThing = TryCreateMaterialIngredient(ingredient, material, level);
                if (materialThing != null)
                    return materialThing;

                try
                {
                    var defaultThingId = ingredient.IdThing;
                    if (!string.IsNullOrWhiteSpace(defaultThingId))
                    {
                        var defaultThing = GameAccess.Spawn.CreateThing(defaultThingId, -1, level);
                        if (defaultThing != null)
                            return defaultThing;
                    }
                }
                catch { }

                try { return GameAccess.Spawn.CreateThingFromCategory(ingredient.id, level); }
                catch { return null; }
            }

            foreach (var id in EnumerateIngredientIds(ingredient))
            {
                try
                {
                    var thing = ingredient.mat >= 0
                        ? GameAccess.Spawn.CreateThing(id, ingredient.mat, level)
                        : GameAccess.Spawn.CreateThing(id, -1, level);
                    if (thing != null)
                        return thing;
                }
                catch { }
            }

            return null;
        }

        private static Thing? TryCreateMaterialIngredient(
            Recipe.Ingredient ingredient,
            SourceMaterial.Row? material,
            int level)
        {
            if (material == null || string.IsNullOrWhiteSpace(material.thing))
                return null;

            Thing? candidate = null;
            try
            {
                candidate = GameAccess.Spawn.CreateThing(material.thing, material.alias, level);
                if (candidate != null && ingredient.IsValidIngredient(candidate))
                    return candidate;
            }
            catch { }

            try { candidate?.Destroy(); } catch { }
            return null;
        }

        private static IEnumerable<string> EnumerateIngredientIds(Recipe.Ingredient ingredient)
        {
            if (!string.IsNullOrWhiteSpace(ingredient.id))
                yield return ingredient.id;

            if (ingredient.idOther == null)
                yield break;

            foreach (var id in ingredient.idOther)
            {
                if (!string.IsNullOrWhiteSpace(id) &&
                    !string.Equals(id, ingredient.id, StringComparison.Ordinal))
                    yield return id;
            }
        }
    }

    [HarmonyPatch]
    private static class SuppressOriginalDismantledOutputPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => OriginalDismantledOutputGuardTarget != null;

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => OriginalDismantledOutputGuardTarget;

        private static bool Prefix(ref bool __result)
        {
            if (!_suppressOriginalDismantledOutput || !IsDismantleMaterialReturnEnabled)
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    private static class DismantleMaterialRandomRollPatch
    {
        private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(
            typeof(EClass),
            "rndf",
            new[] { typeof(float) });

        [HarmonyPrepare]
        private static bool Prepare() => Target != null;

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => Target;

        private static bool Prefix(ref float __result)
        {
            if (!_forceDismantleMaterialRoll || !IsDismantleMaterialReturnEnabled)
                return true;

            __result = GuaranteedGatheringRewardsPolicy.ResolveDismantleRoll(1f, true);
            return false;
        }
    }

    [HarmonyPatch]
    private static class DismantlingRecipeDiscoveryScopePatch
    {
        private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(
            typeof(RecipeManager),
            "ComeUpWithRecipe",
            new[] { typeof(string), typeof(int) });

        [HarmonyPrepare]
        private static bool Prepare() => Target != null;

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => Target;

        private static void Prefix(string __0, out bool __state)
        {
            __state = false;
            if (!IsDismantlingRecipeEnabled)
                return;

            var currentFrame = UnityEngine.Time.frameCount;
            if (_pendingDismantledRecipeFrame != currentFrame)
            {
                _pendingDismantledRecipeId = null;
                _pendingDismantledRecipeFrame = -1;
                return;
            }

            if (!GuaranteedGatheringRewardsPolicy.IsMatchingDismantleRecipeAttempt(
                    true,
                    _pendingDismantledRecipeId,
                    __0,
                    _pendingDismantledRecipeFrame,
                    currentFrame))
                return;

            _pendingDismantledRecipeId = null;
            _pendingDismantledRecipeFrame = -1;
            _forceRecipeDiscoveryRoll = true;
            _recipeDiscoveryRollConsumed = false;
            __state = true;
        }

        private static void Postfix(bool __state)
        {
            if (__state)
            {
                _forceRecipeDiscoveryRoll = false;
                _recipeDiscoveryRollConsumed = false;
            }
        }

        private static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                _forceRecipeDiscoveryRoll = false;
                _recipeDiscoveryRollConsumed = false;
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    private static class DismantlingRecipeRandomRollPatch
    {
        private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(
            typeof(EClass),
            "rnd",
            new[] { typeof(int) });

        [HarmonyPrepare]
        private static bool Prepare() => Target != null;

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod() => Target;

        private static bool Prefix(ref int __result)
        {
            if (!_forceRecipeDiscoveryRoll || _recipeDiscoveryRollConsumed)
                return true;

            _recipeDiscoveryRollConsumed = true;
            __result = 0;
            return false;
        }
    }
}
