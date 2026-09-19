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
    private static string _itemMoreInfoGatheringHoverText = "";
    private static float _itemMoreInfoGatheringHoverNextAt;

    [HarmonyPatch(typeof(WidgetMouseover), "Refresh")]
    private static class WidgetMouseoverRefreshItemMoreInfoGatheringPatch
    {
        private static bool Prefix(WidgetMouseover __instance)
        {
            if (!ShouldShowItemMoreInfo() || Instance == null || !Instance._showItemMoreInfoGatheringThreshold)
                return true;

            try
            {
                if (__instance.roster != null || !CanShowItemMoreInfoGatheringHover())
                    return true;

                var text = MoreInfoModule.BuildStandaloneGatheringThresholdHoverText(
                    GameAccess.Ui.Scene!.mouseTarget.pos);
                if (string.IsNullOrEmpty(text))
                    return true;

                if (!string.Equals(text, _itemMoreInfoGatheringHoverText, StringComparison.Ordinal) ||
                    Time.unscaledTime >= _itemMoreInfoGatheringHoverNextAt)
                {
                    _itemMoreInfoGatheringHoverText = text;
                    _itemMoreInfoGatheringHoverNextAt = Time.unscaledTime + ItemMoreInfoGatheringHoverInterval;
                    _npcMoreInfoExpectedHoverFrame = Time.frameCount;
                    _npcMoreInfoExpectedHoverBlock = text;
                    __instance.Show(text);
                }
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
    private static bool CanShowItemMoreInfoGatheringHover()
    {
        var mouseTarget = GameAccess.Ui.Scene?.mouseTarget;
        if (mouseTarget == null || mouseTarget.card != null || mouseTarget.target != null)
            return false;

        var pos = mouseTarget.pos;
        if (pos == null || !pos.IsValid || pos.IsHidden)
            return false;

        var ui = EClass.ui;
        if (ui == null || ui.BlockMouseOverUpdate)
            return false;
        if (mouseTarget.mouse && (ui.isPointerOverUI || EClass.scene?.actionMode?.ShowMouseoverTarget != true))
            return false;
        return !ActionMode.IsAdv || !Input.GetMouseButton(0);
    }
}
