using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Zone), "Deactivate")]
internal static class MoongatePersistenceZoneDeactivatePatch
{
    [HarmonyPrefix]
    private static void Prefix(Zone __instance)
    {
        MoongatePersistentStorage.SaveBeforeLeaving(__instance);
    }
}

[HarmonyPatch(typeof(TraitMoongate), "ListSavedUserMap")]
internal static class MoongatePersistenceSavedMapListPatch
{
    [HarmonyPostfix]
    private static void Postfix(List<MapMetaData> __result)
    {
        MoongatePersistentStorage.MarkPersistentMaps(__result);
    }
}

[HarmonyPatch(typeof(TraitMoongate), "LoadMap", new[] { typeof(MapMetaData) })]
internal static class MoongatePersistenceLoadMapPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref MapMetaData __0)
    {
        __0 = MoongatePersistentStorage.PrepareMapForLoad(__0);
    }
}

[HarmonyPatch(typeof(TraitMoongate), "MoveZone", new[] { typeof(Zone) })]
internal static class MoongatePersistenceMoveZonePatch
{
    [HarmonyPrefix]
    private static void Prefix(Zone __0)
    {
        MoongatePersistentStorage.PrepareZoneForMove(__0);
    }
}

