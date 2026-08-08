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
    private static void RegisterUnlimitedStethoscopeActs(TraitStethoscope trait, ActPlan plan)
    {
        if (trait == null || plan == null)
            return;
        try
        {
            if (!plan.IsSelfOrNeighbor)
                return;
            var cards = plan.pos?.ListCards(false);
            if (cards == null)
                return;
            foreach (var card in cards)
            {
                var chara = card?.Chara;
                if (chara == null)
                    continue;
                try
                {
                    if (GameAccess.Characters.PlayerCharacter == null || !GameAccess.Characters.PlayerCharacter.CanSee(card))
                        continue;
                }
                catch
                {
                    continue;
                }

                var target = chara;
                plan.TrySetAct(
                    "actInvestigate",
                    () => UseStethoscopeOnTarget(trait, target),
                    target,
                    null,
                    1,
                    false,
                    true,
                    false);
            }
        }
        catch { }
    }
    private static bool UseStethoscopeOnTarget(TraitStethoscope trait, Chara target)
    {
        if (trait == null || target == null)
            return false;
        try
        {
            var owner = trait.owner;
            GameAccess.Characters.PlayerCharacter?.Say("use_scope", target, owner);
            GameAccess.Characters.PlayerCharacter?.Say("use_scope2", target);
            target.Talk("pervert2");
            GameAccess.Ui.Root?.AddLayer<LayerChara>()?.SetChara(target);
            owner?.ModCharge(-1, false);
            if (owner != null && owner.c_charges <= 0)
            {
                GameAccess.Characters.PlayerCharacter?.Say("spellbookCrumble", owner);
                owner.Destroy();
            }
        }
        catch { }
        return false;
    }
}
