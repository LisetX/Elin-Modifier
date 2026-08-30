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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    [HarmonyPatch(typeof(Chara), "GetHoverText")]
    private static class CharaGetHoverTextNpcMoreInfoPatch
    {
        private static void Postfix(Chara __instance, ref string __result)
        {
            if (!ShouldShowNpcMoreInfo() || !ShouldPrefixNpcMoreInfoLevel() || ShouldSkipNpcMoreInfo(__instance))
                return;

            try
            {
                var level = SafeInt(() => __instance.LV, 0).ToString(CultureInfo.InvariantCulture);
                __result = ApplyNpcMoreInfoExtraFontSize("level", ColorNpcMoreInfoText("Lv." + level, NpcMoreInfoLevelColor)) + " " + (__result ?? "");
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(WidgetMouseover), "Show")]
    private static class WidgetMouseoverShowNpcMoreInfoDirectionPatch
    {
        private static void Prefix(WidgetMouseover __instance, ref string s, out bool __state)
        {
            if (ShouldShowItemMoreInfo() && Instance != null &&
                (Instance._showItemMoreInfoPlantStats ||
                 Instance._showItemMoreInfoPlantStatsExtended ||
                 Instance._showItemMoreInfoGatheringThreshold))
            {
                try
                {
                    var mouseTarget = GameAccess.Ui.Scene?.mouseTarget;
                    if (mouseTarget != null && mouseTarget.card == null && mouseTarget.target != null)
                    {
                        var details = "";
                        if (Instance._showItemMoreInfoPlantStats || Instance._showItemMoreInfoPlantStatsExtended)
                            details += BuildPlantMoreInfoHoverDetails(mouseTarget.pos);
                        if (Instance._showItemMoreInfoGatheringThreshold)
                            details += MoreInfoModule.BuildMapGatheringThresholdHoverDetails(mouseTarget.pos);
                        if (!string.IsNullOrEmpty(details))
                        {
                            s = (s ?? "") + details;
                            _npcMoreInfoExpectedHoverFrame = Time.frameCount;
                            _npcMoreInfoExpectedHoverBlock = details;
                        }
                    }
                }
                catch { }
            }

            __state = ConsumeExpectedNpcMoreInfoHover(s);
            ConfigureNpcMoreInfoHoverDirection(__instance, __state);
        }

        private static void Postfix(WidgetMouseover __instance, bool __state)
        {
            if (!__state || __instance.layout == null)
                return;

            try
            {
                switch (__instance.Rect().GetAnchor())
                {
                    case RectPosition.TopLEFT:
                    case RectPosition.BottomLEFT:
                        __instance.layout.childAlignment = TextAnchor.UpperLeft;
                        break;
                    case RectPosition.TopRIGHT:
                    case RectPosition.BottomRIGHT:
                        __instance.layout.childAlignment = TextAnchor.UpperRight;
                        break;
                    default:
                        __instance.layout.childAlignment = TextAnchor.UpperCenter;
                        break;
                }
                __instance.layout.RebuildLayout();
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(Chara), "GetHoverText2")]
    private static class CharaGetHoverText2NpcMoreInfoPatch
    {
        private static void Postfix(Chara __instance, ref string __result)
        {
            if (!ShouldShowNpcMoreInfo() || ShouldSkipNpcMoreInfo(__instance))
                return;

            try
            {
                if (ShouldShowNpcMoreInfoBuffs())
                    __result = RemoveOriginalNpcMoreInfoBuffLine(__instance, __result);
                if (ShouldShowNpcMoreInfoRelationFaith())
                    __result = RemoveOriginalNpcMoreInfoFavoriteLine(__instance, __result);
                var details = BuildNpcMoreInfoHoverDetails(__instance);
                __result = (__result ?? "") + details;
                if (!string.IsNullOrEmpty(details))
                {
                    _npcMoreInfoExpectedHoverFrame = Time.frameCount;
                    _npcMoreInfoExpectedHoverBlock = details;
                }
            }
            catch { }
        }
    }
}
