using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
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
using static ElinModifierPlugin;

internal sealed partial class ExceptionTraceModule
{
    private void AppendDebugKnownPathProbe(StringBuilder sb, List<DebugStackFrameInfo> frames, int frame)
    {
        if (frames == null || frames.Count == 0)
            return;
        var stackText = string.Join("\n", frames.Select(f => (f.TypeName ?? "") + "." + (f.MethodName ?? "")).ToArray());
        if (stackText.IndexOf("FactionBranch.OnSimulateHour", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            sb.AppendLine("Known path probe: FactionBranch.OnSimulateHour");
            var branch = _host.SafeDebugValue(() => GameAccess.World.BranchOrHomeBranch) as FactionBranch;
            if (branch == null)
                branch = _host.SafeDebugValue(() => GameAccess.World.CurrentZone?.branch) as FactionBranch;
            var spawnEvents = GetRecentDebugSubmoduleEvents(frames, frame, 6)
                .Where(e => string.Equals(e.Method, "Zone.SpawnMob", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (spawnEvents.Count > 0)
            {
                var latestSpawn = spawnEvents[spawnEvents.Count - 1];
                sb.AppendLine("Probable exact point: Zone.SpawnMob returned null during FactionBranch.OnSimulateHour, then the original method continued to read the returned Chara.");
                sb.AppendLine("Latest SpawnMob result: " + latestSpawn.Result);
                sb.AppendLine("Latest SpawnMob target: " + latestSpawn.Target);
                if (latestSpawn.Arguments.Length > 0)
                    sb.AppendLine("Latest SpawnMob point: " + latestSpawn.Arguments[0]);
                if (latestSpawn.Arguments.Length > 1)
                    sb.AppendLine("Latest SpawnMob setting: " + latestSpawn.Arguments[1]);
                if (latestSpawn.Arguments.Length > 0 && string.Equals(latestSpawn.Arguments[0], "null", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("Concrete null point: Spawn point argument is null; spawn position generation failed before Chara creation.");
                sb.AppendLine("Concrete failing access: spawned Chara is null; FactionBranch.OnSimulateHour reaches Chara.IsAnimal without a null guard.");
            }
            AppendDebugProbeLine(sb, "Branch", branch);
            if (branch != null)
            {
                AppendDebugProbeLine(sb, "Branch.owner", _host.SafeDebugValue(() => branch.owner));
                AppendDebugProbeLine(sb, "Branch.owner.elements", _host.SafeDebugValue(() => branch.owner?.elements));
                AppendDebugProbeLine(sb, "Branch.stability", _host.SafeDebugValue(() => branch.stability));
                AppendDebugProbeLine(sb, "Branch.resources", _host.SafeDebugValue(() => branch.resources));
                AppendDebugProbeLine(sb, "Branch.researches", _host.SafeDebugValue(() => branch.researches));
                AppendDebugProbeLine(sb, "Branch.policies", _host.SafeDebugValue(() => branch.policies));
                AppendDebugProbeLine(sb, "Branch.happiness", _host.SafeDebugValue(() => branch.happiness));
                AppendDebugProbeLine(sb, "Branch.meetings", _host.SafeDebugValue(() => branch.meetings));
                AppendDebugProbeLine(sb, "Branch.expeditions", _host.SafeDebugValue(() => branch.expeditions));
                AppendDebugProbeLine(sb, "Branch.members.Count", _host.SafeDebugValue(() => branch.members == null ? null : (object)branch.members.Count));
                AppendDebugProbeLine(sb, "Branch.members.nullItems", _host.SafeDebugValue(() => CountDebugNullItems(branch.members)));
                AppendDebugProbeLine(sb, "Branch.listRecruit.Count", _host.SafeDebugValue(() => branch.listRecruit == null ? null : (object)branch.listRecruit.Count));
                AppendDebugProbeLine(sb, "Branch.statistics", _host.SafeDebugValue(() => branch.statistics));
                AppendDebugProbeLine(sb, "Branch.lastStatistics", _host.SafeDebugValue(() => branch.lastStatistics));
                AppendDebugProbeLine(sb, "Branch.log", _host.SafeDebugValue(() => branch.log));
                AppendDebugProbeLine(sb, "Branch.faith", _host.SafeDebugValue(() => branch.faith));
                AppendDebugProbeLine(sb, "Branch.stash", _host.SafeDebugValue(() => branch.stash));
            }
            if (spawnEvents.Count == 0)
                sb.AppendLine("No nearby Zone.SpawnMob null-result event was captured; inspect the branch member/manager probes above for null state.");
        }
        if (stackText.IndexOf("Zone.SpawnMob", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            sb.AppendLine("Known path probe: Zone.SpawnMob");
            AppendDebugProbeLine(sb, "EClass._zone", _host.SafeDebugValue(() => GameAccess.World.CurrentZone));
            AppendDebugProbeLine(sb, "EClass._map", _host.SafeDebugValue(() => GameAccess.World.CurrentMap));
            AppendDebugProbeLine(sb, "Map.bounds", _host.SafeDebugValue(() => GetDebugMemberValue(GameAccess.World.CurrentMap, "bounds")));
            sb.AppendLine("If SpawnMob returns null, caller code must null-check before reading Chara fields/properties.");
        }
    }
    private void AppendDebugNullMemberProbe(StringBuilder sb, string title, Type type, object instance, bool staticOnly, int limit)
    {
        if (type == null)
            return;
        var found = 0;
        foreach (var member in _host.GetDebugMembers(type, staticOnly))
        {
            if (member == null || IsDebugLeafType(member.ValueType))
                continue;
            object value = null;
            var ok = true;
            try { value = member.GetValue(staticOnly ? null : instance); }
            catch { ok = false; }
            if (!ok || value != null)
                continue;
            if (found == 0)
                sb.AppendLine(title + ":");
            sb.AppendLine("  [NULL] " + member.Kind + " " + member.Name + " : " + GetDebugTypeName(member.ValueType));
            found++;
            if (found >= limit)
            {
                sb.AppendLine("  ... truncated");
                break;
            }
        }
        if (found == 0)
            sb.AppendLine(title + ": none detected");
    }
    internal static int CountDebugNullItems(IEnumerable enumerable)
    {
        if (enumerable == null)
            return 0;
        var count = 0;
        try
        {
            foreach (var item in enumerable)
                if (item == null)
                    count++;
        }
        catch { }
        return count;
    }
    internal static int CountDebugCollectionItems(object collection, int maxCount)
    {
        if (collection == null)
            return -1;
        try
        {
            if (collection is ICollection genericCollection)
                return genericCollection.Count;
        }
        catch { }
        try
        {
            if (collection is Array array)
                return array.Length;
        }
        catch { }
        try
        {
            if (collection is IEnumerable enumerable)
            {
                var count = 0;
                foreach (var _ in enumerable)
                {
                    count++;
                    if (count >= maxCount)
                        return count;
                }
                return count;
            }
        }
        catch { }
        return -1;
    }
    private static void AppendDebugProbeLine(StringBuilder sb, string label, object value)
    {
        sb.Append(value == null ? "[NULL] " : "[OK] ");
        sb.Append(label).Append(" = ").AppendLine(DescribeDebugTraceValue(value));
    }
}
