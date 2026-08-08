using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[HarmonyPatch(typeof(Zone), "Import", new[] { typeof(string) })]
internal static class MoongateContainerImportPreparePatch
{
    [HarmonyPrefix]
    private static void Prefix(Zone __instance)
    {
        MoongateContainerTransfer.ClearExtractedPayload(__instance);
    }
}

[HarmonyPatch(typeof(Map), "OnImport", new[] { typeof(ZoneExportData) })]
internal static class MoongateContainerImportRestorePatch
{
    [HarmonyPostfix]
    private static void Postfix(Map __instance)
    {
        MoongateContainerTransfer.RestoreExtractedPayload(__instance, __instance.zone);
    }
}

