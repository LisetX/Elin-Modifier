using System;
using HarmonyLib;
using UnityEngine;

internal sealed class MerchantMonsterBallModule
{
    internal bool Enabled { get; private set; }
    internal bool LevelOptimizationEnabled { get; private set; }

    internal void Load(bool enabled, bool levelOptimizationEnabled)
    {
        Enabled = enabled;
        LevelOptimizationEnabled = levelOptimizationEnabled;
    }

    internal void Reset()
    {
        Enabled = false;
        LevelOptimizationEnabled = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }

    internal bool SetLevelOptimizationEnabled(bool enabled)
    {
        if (LevelOptimizationEnabled == enabled)
            return false;
        LevelOptimizationEnabled = enabled;
        return true;
    }

    internal bool IsGoodsMerchantRestock(Trait? trait)
    {
        if ((!Enabled && !LevelOptimizationEnabled) ||
            trait?.owner == null ||
            trait.ShopType != ShopType.Goods)
            return false;

        try
        {
            return GameAccess.World.CurrentWorld?.date != null &&
                   GameAccess.World.CurrentWorld.date.IsExpired(trait.owner.c_dateStockExpire) &&
                   !(trait.RestockDay < 0 && trait.owner.isRestocking);
        }
        catch
        {
            return false;
        }
    }

    internal void ApplyRestockChanges(Trait? trait)
    {
        if ((!Enabled && !LevelOptimizationEnabled) ||
            trait?.owner == null ||
            trait.ShopType != ShopType.Goods)
            return;

        try
        {
            var chest = trait.owner.things.Find("chest_merchant");
            if (chest == null)
                return;

            if (Enabled && chest.things.Find("monsterball") == null)
            {
                var width = Mathf.Max(1, chest.things.width);
                var maximumVisibleCount = width * 10;
                if (chest.things.Count >= maximumVisibleCount)
                {
                    Thing? replace = null;
                    foreach (var thing in chest.things)
                    {
                        if (thing != null && thing.GetInt(101) == 0)
                            replace = thing;
                    }
                    replace?.Destroy();
                }

                CardBlueprint.SetNormalRarity();
                chest.AddThing(GameAccess.Spawn.CreateThing("monsterball", -1, trait.ShopLv), tryStack: true);

                if (chest.things.Count > chest.things.GridSize)
                {
                    var height = Mathf.Min(
                        10,
                        Mathf.Max(1, (chest.things.Count + width - 1) / width));
                    chest.things.ChangeSize(width, height);
                }
            }

            if (LevelOptimizationEnabled && GameAccess.Characters.PlayerCharacter != null)
            {
                var pcLevel = Math.Max(0, GameAccess.Characters.PlayerCharacter.LV);
                foreach (var thing in chest.things)
                {
                    if (thing?.id != "monsterball")
                        continue;
                    var optimizedLevel = (int)Math.Min(
                        int.MaxValue,
                        (long)Math.Max(1, thing.LV) + pcLevel);
                    thing.SetLv(optimizedLevel);
                }
            }
        }
        catch
        {
        }
    }
}

internal static class MerchantMonsterBallPatchContext
{
    internal static MerchantMonsterBallModule? Current =>
        ElinModifierPlugin.ActiveModules?.MerchantMonsterBall;
}

[HarmonyPatch(typeof(Trait), "OnBarter")]
internal static class TraitOnBarterMerchantMonsterBallPatch
{
    private static void Prefix(Trait __instance, ref bool __state)
    {
        __state = MerchantMonsterBallPatchContext.Current?.IsGoodsMerchantRestock(__instance) == true;
    }

    private static void Postfix(Trait __instance, bool __state)
    {
        if (__state)
            MerchantMonsterBallPatchContext.Current?.ApplyRestockChanges(__instance);
    }
}
