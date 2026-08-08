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
    private void RunDebugStabilityTest()
    {
        if (!_debugAuthorized)
            return;

        var sb = new StringBuilder(12000);
        var okCount = 0;
        var warningCount = 0;
        var errorCount = 0;
        var stabilityScore = 100;
        var scoreEvents = new List<string>();
        var modifierActiveFeatureCount = 0;
        var modifierIssueCount = 0;
        var modifierEvidencePoints = 0;

        void DeductScore(string label, string detail, int points)
        {
            points = Math.Max(0, points);
            if (points <= 0)
                return;
            stabilityScore = Math.Max(0, stabilityScore - points);
            scoreEvents.Add("-" + points.ToString(CultureInfo.InvariantCulture) + " " + label + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
        }

        void Ok(string label, object value = null)
        {
            okCount++;
            sb.Append("[OK] ").Append(label);
            if (value != null)
                sb.Append(" = ").Append(DescribeDebugTraceValue(value));
            sb.AppendLine();
        }

        void Warn(string label, string detail, int penalty = 2)
        {
            warningCount++;
            DeductScore(label, detail, penalty);
            sb.Append("[WARN] ").Append(label);
            if (!string.IsNullOrEmpty(detail))
                sb.Append(" - ").Append(detail);
            sb.AppendLine();
        }

        void Error(string label, string detail, int penalty = 10)
        {
            errorCount++;
            DeductScore(label, detail, penalty);
            sb.Append("[ERROR] ").Append(label);
            if (!string.IsNullOrEmpty(detail))
                sb.Append(" - ").Append(detail);
            sb.AppendLine();
        }

        void CheckObject(string label, object value, bool required)
        {
            if (value != null)
                Ok(label, value);
            else if (required)
                Error(label, "null", 12);
            else
                Warn(label, "null");
        }

        void CheckCollection(string label, object collection, bool required)
        {
            if (collection == null)
            {
                if (required)
                    Error(label, "null", 12);
                else
                    Warn(label, "null");
                return;
            }

            var count = CountDebugCollectionItems(collection, 200000);
            if (count >= 0)
                Ok(label + ".Count", count.ToString(CultureInfo.InvariantCulture));
            else
                Warn(label + ".Count", "unavailable");

            var nullItems = CountDebugNullItems(collection as IEnumerable);
            if (nullItems > 0)
                Warn(label + ".nullItems", nullItems.ToString(CultureInfo.InvariantCulture), Math.Min(8, 2 + nullItems));
            else
                Ok(label + ".nullItems", "0");
        }

        int CountEnabledFlags(Dictionary<string, bool> flags)
        {
            if (flags == null || flags.Count == 0)
                return 0;
            var count = 0;
            foreach (var pair in flags)
                if (pair.Value)
                    count++;
            return count;
        }

        void ModifierImpact(string label, bool active, string detail, int penalty)
        {
            if (!active)
                return;
            modifierActiveFeatureCount++;
            Ok("Modifier feature active: " + label, detail);
        }

        void ModifierState(string label, bool risky, string detail, int penalty)
        {
            if (risky)
            {
                modifierIssueCount++;
                modifierEvidencePoints += Math.Max(0, penalty);
                Warn("Modifier evidence: " + label, detail, penalty);
            }
            else
            {
                Ok("Modifier state: " + label, detail);
            }
        }

        bool IsModifierErrorRecord(DebugExceptionTraceRecord record)
        {
            if (record == null)
                return false;
            var text = (record.Source ?? "") + "\n" + (record.Level ?? "") + "\n" + (record.Trace ?? "");
            return text.IndexOf("ElinModifierPlugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Elin Modifier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("local.elin.modifier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string BuildModifierErrorFeatureSummary()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in _debugExceptionTraceRecords)
            {
                if (!IsModifierErrorRecord(record))
                    continue;
                var trace = record.Trace ?? "";
                void AddFeature(string name, params string[] needles)
                {
                    foreach (var needle in needles)
                    {
                        if (trace.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        counts.TryGetValue(name, out var count);
                        counts[name] = count + 1;
                        return;
                    }
                }

                AddFeature("Locks", "ApplyLocks", "ApplyValueSilently");
                AddFeature("Debug locks", "ApplyDebugLocks", "DebugBinding");
                AddFeature("Infinite sight", "ApplyInfinitePlayerSight", "Fov", "Sight");
                AddFeature("Food decay/rot", "FoodRot", "DecayNatural", "ShouldKeepFoodFresh");
                AddFeature("Crafting", "Recipe", "Craft", "Ingredient", "ThingStack");
                AddFeature("Ability overrides", "Ability", "CalcCastingChance", "GetCost", "UseAbility");
                AddFeature("Item/food/weapon/gene editor", "ItemAmount", "ItemDataEditor", "FoodEditor", "WeaponEditor", "GeneEditor");
                AddFeature("Threat marker", "ThreatMarker", "Hostile");
                AddFeature("Frame rate", "FrameRate", "targetFrameRate", "vSync");
            }

            if (counts.Count == 0)
                return "No feature-specific stack marker detected.";
            var sbFeature = new StringBuilder();
            var first = true;
            foreach (var pair in counts)
            {
                if (!first)
                    sbFeature.Append(", ");
                first = false;
                sbFeature.Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sbFeature.ToString();
        }

        try
        {
            var frame = 0;
            try { frame = Time.frameCount; } catch { }
            sb.AppendLine("Game and Plugin Stability Test");
            sb.AppendLine("Frame: " + frame.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("Mode: passive diagnostic snapshot; no game data is modified.");
            sb.AppendLine();

            sb.AppendLine("Runtime");
            try
            {
                var processName = Process.GetCurrentProcess().ProcessName;
                if (string.Equals(processName, "Elin", StringComparison.OrdinalIgnoreCase))
                    Ok("Process", processName);
                else
                    Warn("Process", processName, 8);
            }
            catch (Exception ex)
            {
                Warn("Process", ex.GetType().Name, 4);
            }
            Ok("Plugin version", ModMetadata.Version);
            Ok("Debug authorized", _debugAuthorized.ToString(CultureInfo.InvariantCulture));
            Ok("Debug visible", _debugVisible.ToString(CultureInfo.InvariantCulture));
            var coreRegression = CoreRegressionTests.Run();
            if (coreRegression.Success)
            {
                Ok("Core regression tests",
                    coreRegression.Passed.ToString(CultureInfo.InvariantCulture) + " passed");
            }
            else
            {
                Error("Core regression tests",
                    coreRegression.Failed.ToString(CultureInfo.InvariantCulture) + " failed: " +
                    string.Join(", ", coreRegression.Failures.ToArray()), 12);
            }
            Ok("Captured error records", _debugExceptionTraceRecords.Count.ToString(CultureInfo.InvariantCulture));
            if (_debugExceptionTraceRecords.Count > 0)
            {
                var latest = _debugExceptionTraceRecords[_debugExceptionTraceRecords.Count - 1];
                Warn("Latest captured error", latest.Level + " | " + latest.Source + " | frame " + latest.Frame.ToString(CultureInfo.InvariantCulture), Math.Min(20, 4 + _debugExceptionTraceRecords.Count * 2));
            }
            sb.AppendLine();

            sb.AppendLine("Game context");
            var pc = SafeDebugValue(() => GameAccess.Characters.PlayerCharacter);
            var player = SafeDebugValue(() => GameAccess.Runtime.Player);
            var world = SafeDebugValue(() => GameAccess.World.CurrentWorld);
            var scene = SafeDebugValue(() => GameAccess.Ui.Scene);
            var zone = SafeDebugValue(() => GameAccess.World.CurrentZone);
            var map = SafeDebugValue(() => GameAccess.World.CurrentMap);
            var sources = SafeDebugValue(() => GameAccess.Sources.Manager);
            CheckObject("EClass.pc", pc, false);
            CheckObject("EClass.player", player, false);
            CheckObject("EClass.world", world, false);
            CheckObject("EClass.scene", scene, false);
            CheckObject("EClass._zone", zone, false);
            CheckObject("EClass._map", map, false);
            CheckObject("EClass.sources", sources, false);

            if (map != null)
            {
                CheckCollection("Map.charas", SafeDebugValue(() => ((Map)map).charas), true);
                CheckCollection("Map.things", SafeDebugValue(() => ((Map)map).things), true);
                CheckCollection("Map.cells", SafeDebugValue(() => ((Map)map).cells), false);
            }

            var branch = SafeDebugValue(() => GameAccess.World.BranchOrHomeBranch) as FactionBranch;
            if (branch == null)
            {
                Warn("Current BranchOrHomeBranch", "null", map == null ? 1 : 4);
            }
            else
            {
                Ok("Current BranchOrHomeBranch", branch);
                CheckObject("Branch.owner", SafeDebugValue(() => branch.owner), true);
                CheckObject("Branch.owner.elements", SafeDebugValue(() => branch.owner?.elements), true);
                CheckObject("Branch.stability", SafeDebugValue(() => branch.stability), true);
                CheckObject("Branch.resources", SafeDebugValue(() => branch.resources), true);
                CheckObject("Branch.researches", SafeDebugValue(() => branch.researches), false);
                CheckObject("Branch.policies", SafeDebugValue(() => branch.policies), true);
                CheckObject("Branch.happiness", SafeDebugValue(() => branch.happiness), false);
                CheckObject("Branch.meetings", SafeDebugValue(() => branch.meetings), true);
                CheckObject("Branch.expeditions", SafeDebugValue(() => branch.expeditions), true);
                CheckCollection("Branch.members", SafeDebugValue(() => branch.members), true);
                CheckCollection("Branch.listRecruit", SafeDebugValue(() => branch.listRecruit), false);
                CheckObject("Branch.statistics", SafeDebugValue(() => branch.statistics), true);
                CheckObject("Branch.lastStatistics", SafeDebugValue(() => branch.lastStatistics), false);
                CheckObject("Branch.log", SafeDebugValue(() => branch.log), false);
                CheckObject("Branch.faith", SafeDebugValue(() => branch.faith), false);
                CheckObject("Branch.stash", SafeDebugValue(() => branch.stash), false);
            }
            sb.AppendLine();

            sb.AppendLine("Source database");
            CheckCollection("SourceThing rows", SafeDebugValue(() => GetDebugMemberValue(GameAccess.Sources.Things, "rows")), false);
            CheckCollection("SourceChara rows", SafeDebugValue(() => GetDebugMemberValue(GameAccess.Sources.Characters, "rows")), false);
            CheckCollection("SourceElement rows", SafeDebugValue(() => GetDebugMemberValue(GameAccess.Sources.Elements, "rows")), false);
            CheckCollection("SourceMaterial rows", SafeDebugValue(() => GetDebugMemberValue(GameAccess.Sources.Materials, "rows")), false);
            CheckCollection("SourceRecipe rows", SafeDebugValue(() => GetDebugMemberValue(GetDebugMemberValue(GameAccess.Sources.Manager, "recipes"), "rows")), false);
            CheckCollection("SourceZone rows", SafeDebugValue(() => GetDebugMemberValue(GetDebugMemberValue(GameAccess.Sources.Manager, "zones"), "rows")), false);
            sb.AppendLine();

            sb.AppendLine("Elin Modifier impact analysis");
            if (_modules.Harmony.IsInstalled)
            {
                ModifierImpact(
                    "Harmony patches installed",
                    true,
                    _modules.Harmony.InstalledPatchCount.ToString(CultureInfo.InvariantCulture) + "/" +
                    _modules.Harmony.DiscoveredPatchCount.ToString(CultureInfo.InvariantCulture) +
                    " isolated patch classes installed.",
                    0);
                if (_modules.Harmony.Failures.Count > 0)
                    Warn("Harmony isolated patch failures",
                        _modules.Harmony.Failures.Count.ToString(CultureInfo.InvariantCulture), 6);
            }
            else
            {
                Error("Modifier impact: Harmony patches", "not installed", 8);
            }

            var modifierErrorCount = 0;
            foreach (var record in _debugExceptionTraceRecords)
                if (IsModifierErrorRecord(record))
                    modifierErrorCount++;
            if (modifierErrorCount > 0)
            {
                modifierIssueCount++;
                var featureSummary = BuildModifierErrorFeatureSummary();
                modifierEvidencePoints += Math.Min(24, 8 + modifierErrorCount * 3);
                Warn(
                    "Modifier evidence: captured modifier-related errors",
                    modifierErrorCount.ToString(CultureInfo.InvariantCulture) + " record(s); " + featureSummary,
                    Math.Min(24, 8 + modifierErrorCount * 3));
            }
            else
            {
                Ok("Modifier evidence: captured modifier-related errors", "0");
            }

            ModifierImpact(
                "Unlock refresh rate limit",
                _unlockFrameRate || _frameRateLimitSaved,
                "Active/saved frame-rate override state. No deduction without restore failure or related errors.",
                0);
            ModifierImpact(
                "Force game unfocus",
                _forceGameUnfocus && IsModifierUiActuallyDrawn(),
                "Active while modifier UI is open. No deduction without input/focus error evidence.",
                0);
            ModifierImpact(
                "Low performance mode",
                _lowPerformanceMode,
                "Active. No deduction; this is an intended optimization mode.",
                0);
            ModifierImpact(
                "Hostile threat marker",
                _hostileThreatMarker,
                "Active. No deduction without related rendering/error evidence.",
                0);
            ModifierImpact(
                "Ignore fog + infinite sight",
                _infinitePlayerSight,
                "Active. No deduction unless related errors or stale applied-state evidence exist.",
                0);
            ModifierState(
                "Infinite sight applied map state",
                !_infinitePlayerSight && (_infinitePlayerSightApplied || _infinitePlayerSightPointCount > 0 || _infinitePlayerSightOriginalTelepathyVisibility.Count > 0),
                "applied=" + _infinitePlayerSightApplied.ToString(CultureInfo.InvariantCulture) +
                ", seenPoints=" + _infinitePlayerSightPointCount.ToString(CultureInfo.InvariantCulture) +
                ", trackedCharaVisibility=" + _infinitePlayerSightOriginalTelepathyVisibility.Count.ToString(CultureInfo.InvariantCulture) +
                (_infinitePlayerSight ? "; active state, not counted as issue" : "; feature off but state remains"),
                5);

            ModifierImpact(
                "Show food rot",
                _showFoodRot,
                "Active. No deduction without overlay/note errors.",
                0);
            ModifierImpact(
                "Ignore food decay",
                _ignoreFoodDecay,
                "Active. No deduction without related errors.",
                0);
            ModifierImpact(
                "Craft without materials",
                _noCraftMaterials,
                "Active. No deduction without crafting errors or stale virtual item evidence.",
                0);
            ModifierImpact(
                "Unlock all craft materials",
                _unlockAllCraftMaterials,
                "Active. No deduction unless related errors are captured.",
                0);
            ModifierImpact(
                "Unlock all craft recipes",
                _unlockAllCraftRecipes,
                "Active. No deduction without recipe errors.",
                0);
            ModifierState(
                "Virtual craft items",
                !_noCraftMaterials && _virtualCraftThingUids.Count > 0,
                _virtualCraftThingUids.Count.ToString(CultureInfo.InvariantCulture) + " tracked virtual item uid(s)",
                Math.Min(10, 3 + _virtualCraftThingUids.Count));

            ModifierImpact(
                "Custom item amount interaction",
                _customItemAmount,
                "Active. Menu entry only; no deduction without related errors.",
                0);
            ModifierImpact(
                "Custom item data interaction",
                _customItemEditor,
                "Active. Menu entry only; no deduction without related errors.",
                0);
            ModifierImpact(
                "Custom food data interaction",
                _customFoodEditor,
                "Active. Menu entry only; no deduction without related errors.",
                0);
            ModifierImpact(
                "Custom weapon data interaction",
                _customWeaponEditor,
                "Active. Menu entry only; no deduction without related errors.",
                0);
            ModifierImpact(
                "Custom gene editor interaction",
                _customGeneEditor,
                "Active. Menu entry only; no deduction without related errors.",
                0);
            ModifierImpact(
                "Stethoscope no target limit",
                _stethoscopeNoTargetLimit,
                "Active. No deduction without related errors.",
                0);
            var normalLockCount = CountEnabledFlags(_locks);
            ModifierState(
                "Player/NPC value locks",
                normalLockCount > 0 && modifierErrorCount > 0,
                normalLockCount.ToString(CultureInfo.InvariantCulture) + " active lock(s); only counted if modifier errors exist",
                Math.Min(8, 2 + normalLockCount));
            var debugLockCount = IsDebugModeActive() ? CountEnabledFlags(_debugLocks) : 0;
            ModifierState(
                "Debug locks",
                debugLockCount > 0 && modifierErrorCount > 0,
                debugLockCount.ToString(CultureInfo.InvariantCulture) + " active debug lock(s); only counted if modifier errors exist",
                Math.Min(12, 4 + debugLockCount));
            var abilityOverrideCount = _abilityChanceOverrides.Count + _abilityPowerOverrides.Count + _abilityCostOverrides.Count;
            ModifierState(
                "Ability/spell runtime overrides",
                abilityOverrideCount > 0 && modifierErrorCount > 0,
                "chance=" + _abilityChanceOverrides.Count.ToString(CultureInfo.InvariantCulture) +
                ", power=" + _abilityPowerOverrides.Count.ToString(CultureInfo.InvariantCulture) +
                ", cost=" + _abilityCostOverrides.Count.ToString(CultureInfo.InvariantCulture) +
                "; only counted if modifier errors exist",
                Math.Min(10, 3 + abilityOverrideCount));

            if (_debugAuthorized)
            {
                ModifierImpact(
                    "Debug error log capture",
                    _debugUnityLogCaptureInstalled || _debugBepInExErrorLogListener != null,
                    "Captures Error/Fatal logs for trace analysis. Passive; no deduction.",
                    0);
            }

            sb.AppendLine("Modifier active features: " + modifierActiveFeatureCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Modifier evidence issues: " + modifierIssueCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Modifier evidence points: " + modifierEvidencePoints.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Modifier causality tier: " + GetDebugModifierRiskTier(modifierEvidencePoints));
            sb.AppendLine();

            sb.AppendLine("Loaded plugins");
            var plugins = GetOtherLoadedBepInExPluginsCached();
            Ok("Other BepInEx mods", plugins.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var plugin in plugins)
            {
                if (plugin == null)
                    continue;
                var name = GetDebugBepInExPluginDisplayName(plugin);
                var guid = GetDebugPluginGuid(plugin.Info);
                var version = GetDebugPluginVersion(plugin.Info);
                var location = GetDebugPluginLocation(plugin.Info);
                var instance = plugin.Instance ?? SafeDebugValue(() => plugin.Info?.Instance);
                if (string.IsNullOrEmpty(guid))
                    Warn("Plugin GUID: " + name, "empty", 1);
                else
                    Ok("Plugin: " + name, guid + " " + version);
                if (instance == null)
                    Warn("Plugin instance: " + name, "null", 2);
                if (string.IsNullOrEmpty(location))
                    Warn("Plugin location: " + name, "empty", 1);
                else if (!File.Exists(location))
                    Warn("Plugin location: " + name, "file not found: " + location, 3);
            }
            sb.AppendLine();

            sb.AppendLine("BepInEx config files");
            var configPath = GetDebugBepInExConfigPath();
            if (string.IsNullOrEmpty(configPath))
            {
                Error("Config path", "empty", 8);
            }
            else if (!Directory.Exists(configPath))
            {
                Error("Config path", "not found: " + configPath, 8);
            }
            else
            {
                Ok("Config path", configPath);
                var files = GetDebugConfigFilesCached(configPath);
                Ok("Config file count", files.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var file in files)
                {
                    try
                    {
                        var entries = GetDebugRawConfigEntries(file);
                        Ok("Config: " + Path.GetFileName(file), entries.Length.ToString(CultureInfo.InvariantCulture) + " entries");
                    }
                    catch (Exception ex)
                    {
                        Warn("Config: " + Path.GetFileName(file), ex.GetType().Name + " - " + ex.Message, 3);
                    }
                }
            }
            sb.AppendLine();

            sb.AppendLine("Reflection scan");
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Ok("Loaded assemblies", assemblies.Length.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Warn("Loaded assemblies", ex.GetType().Name);
            }
            try
            {
                EnsureDebugGameTypeEntries();
                Ok("Reflected game/debug types", (_debugGameTypeEntries == null ? 0 : _debugGameTypeEntries.Count).ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Error("Reflected game/debug types", ex.GetType().Name + " - " + ex.Message, 8);
            }
        }
        catch (Exception ex)
        {
            Error("Stability test runner", ex.GetType().FullName + " - " + ex.Message, 20);
        }

        sb.AppendLine();
        sb.AppendLine("Summary: ok=" + okCount.ToString(CultureInfo.InvariantCulture) +
                      ", warnings=" + warningCount.ToString(CultureInfo.InvariantCulture) +
                      ", errors=" + errorCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Score: " + stabilityScore.ToString(CultureInfo.InvariantCulture) + " / 100");
        sb.AppendLine("Rating: " + GetDebugStabilityRating(stabilityScore));
        sb.AppendLine("Score details:");
        if (scoreEvents.Count == 0)
        {
            sb.AppendLine("No deductions.");
        }
        else
        {
            for (var i = 0; i < scoreEvents.Count; i++)
                sb.AppendLine(scoreEvents[i]);
        }
        _debugStabilityTestResult = sb.ToString();
        _debugLog = "Stability test completed";
    }
    private static string GetDebugStabilityRating(int score)
    {
        if (score >= 95)
            return "Excellent";
        if (score >= 85)
            return "Good";
        if (score >= 70)
            return "Warning";
        if (score >= 50)
            return "Risky";
        return "Critical";
    }
    private static string GetDebugModifierRiskTier(int riskPoints)
    {
        if (riskPoints <= 3)
            return "Low";
        if (riskPoints <= 12)
            return "Moderate";
        if (riskPoints <= 28)
            return "High";
        return "Critical";
    }
    internal object SafeDebugValue(Func<object> getter)
    {
        try { return getter(); }
        catch { return null; }
    }
}
