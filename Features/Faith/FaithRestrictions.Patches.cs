using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    [HarmonyPatch(typeof(TraitAltar), "_OnOffer")]
    private static class TraitAltarUnlimitedOfferingFaithPointsPatch
    {
        private static bool Prefix(TraitAltar __instance, Chara __0, Thing __1, int __2)
        {
            var instance = Instance;
            if (instance == null || !instance._unlimitedOfferingFaithPoints)
                return true;

            try
            {
                var c = __0;
                var t = __1;
                var takeoverMod = __2;
                if (c == null || t == null)
                    return true;

                var affectsKarma = t.GetBool(115);
                var offeringValue = __instance.Deity.GetOfferingValue(t, t.Num);
                offeringValue = offeringValue * (GameAccess.Runtime.Debug.enable ? 1000 : (c.HasElement(1228) ? 130 : 100)) / 100;
                if (takeoverMod == 0)
                {
                    if (offeringValue >= 200)
                    {
                        GameAccess.Messages.Say("god_offer1", t);
                        GameAccess.Characters.PlayerCharacter.faith.Talk("offer");
                    }
                    else if (offeringValue >= 100)
                    {
                        GameAccess.Messages.Say("god_offer2", t);
                    }
                    else if (offeringValue >= 50)
                    {
                        GameAccess.Messages.Say("god_offer3", t);
                    }
                    else
                    {
                        GameAccess.Messages.Say("god_offer4", t);
                    }
                }
                else
                {
                    GameAccess.Messages.Say("god_offer1", t);
                    offeringValue += __instance.Deity.GetOfferingValue(t, 1) * takeoverMod;
                }

                var faithSkill = Mathf.Max(c.Evalue(306), 1);
                var piety = c.elements.GetOrCreateElement(85);
                var previousValue = piety.Value;

                if (piety.vBase < faithSkill)
                {
                    ApplyOfferingPietyExperienceWithoutSingleGainLimit(c, piety, offeringValue * 2 / 3, faithSkill);
                    if (piety.vBase >= faithSkill)
                        c.elements.SetBase(piety.id, faithSkill);
                }

                var messageRank = 4;
                if (piety.vBase < faithSkill)
                    messageRank = Mathf.Clamp(piety.vBase * 100 / faithSkill / 25, 0, 3);
                if (messageRank == 4 || piety.Value != previousValue)
                    GameAccess.Messages.Say("piety" + messageRank, c, c.faith.TextGodGender);

                UnityEngine.Debug.Log(offeringValue + "/" + piety.Value + "/" + piety.vExp);
                if (piety.Value > faithSkill * 8 / 10)
                    c.elements.ModExp(306, offeringValue / 5);
                c.RefreshFaithElement();
                if (c.faith.GetGiftRank() != -1)
                    c.faith.Talk("like");
                if (affectsKarma)
                    GameAccess.Runtime.Player.ModKarma(-1);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void ApplyOfferingPietyExperienceWithoutSingleGainLimit(Chara c, Element piety, int rawExperience, int maxPiety)
        {
            if (rawExperience <= 0 || piety.vBase >= maxPiety)
                return;

            long remaining = rawExperience;
            var guard = 0;
            while (remaining > 0 && piety.vBase < maxPiety && guard++ < 100000)
            {
                var factor = Math.Max(1, c.GetDaysTogetherBonus()) / 100d;
                if (piety.UseExpMod)
                {
                    var potential = Mathf.Clamp(piety.UsePotential ? piety.Potential : 100, 10, 1000);
                    factor *= potential / (double)(100 + Mathf.Max(0, piety.ValueWithoutLink) * 25);
                }

                factor = Math.Max(factor, 0.000001d);
                var experienceToNext = Math.Max(1, piety.ExpToNext - piety.vExp);
                var rawToNext = Math.Max(1L, (long)Math.Ceiling(experienceToNext / factor));
                var chunk = (int)Math.Min(remaining, Math.Min(rawToNext, int.MaxValue));
                c.elements.ModExp(piety.id, chunk);
                remaining -= chunk;
            }
        }
    }
    [HarmonyPatch(typeof(ElementContainerFaction), "IsEffective")]
    private static class ElementContainerFactionIgnoreGodArtifactFaithPatch
    {
        private static void Postfix(Thing __0, ref bool __result)
        {
            var instance = Instance;
            if (instance == null || !instance._ignoreGodArtifactFaithRequirement || __0 == null)
                return;
            if (__0.HasTag(CTAG.godArtifact))
                __result = true;
        }
    }
    [HarmonyPatch(typeof(Element), "IsActive")]
    private static class ElementIgnoreGodArtifactFaithPatch
    {
        private static void Postfix(Element __instance, Card __0, ref bool __result)
        {
            var instance = Instance;
            if (instance == null || !instance._ignoreGodArtifactFaithRequirement || !(__0 is Thing thing))
                return;
            if (thing.HasTag(CTAG.godArtifact))
                __result = __instance.Value != 0;
        }
    }
}
