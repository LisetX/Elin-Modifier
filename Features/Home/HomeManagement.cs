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
    private FactionBranch? GetSelectedHomeBranch(List<FactionBranch> homes)
    {
        if (homes == null || homes.Count == 0)
            return null;

        foreach (var home in homes)
        {
            if (GetHomeBranchKey(home) == _selectedHomeUid)
                return home;
        }

        var preferred = FindPreferredHomeBranch(homes);
        if (preferred == null)
            preferred = homes[0];

        _selectedHomeUid = GetHomeBranchKey(preferred);
        return preferred;
    }
    private static FactionBranch? FindPreferredHomeBranch(List<FactionBranch> homes)
    {
        try
        {
            var current = GameAccess.World.BranchOrHomeBranch;
            foreach (var home in homes)
                if (ReferenceEquals(home, current))
                    return home;
        }
        catch { }

        try
        {
            var homeBranch = GameAccess.Characters.PlayerCharacter?.homeBranch;
            foreach (var home in homes)
                if (ReferenceEquals(home, homeBranch))
                    return home;
        }
        catch { }

        return null;
    }
    private static List<FactionBranch> GetPlayerHomeBranches()
    {
        var homes = new List<FactionBranch>();
        var seen = new HashSet<int>();

        try { AddHomeBranches(homes, seen, GameAccess.Characters.PlayerCharacter?.faction?.GetChildren()); } catch { }
        try { AddHomeBranches(homes, seen, GameAccess.World.Home?.GetChildren()); } catch { }
        try { AddHomeBranch(homes, seen, GameAccess.Characters.PlayerCharacter?.homeBranch); } catch { }
        try { AddHomeBranch(homes, seen, GameAccess.World.BranchOrHomeBranch); } catch { }

        homes.Sort(CompareHomeBranches);
        return homes;
    }
    private static void AddHomeBranches(List<FactionBranch> homes, HashSet<int> seen, IEnumerable<FactionBranch>? branches)
    {
        if (branches == null) return;
        foreach (var branch in branches)
            AddHomeBranch(homes, seen, branch);
    }
    private static void AddHomeBranch(List<FactionBranch> homes, HashSet<int> seen, FactionBranch? branch)
    {
        if (branch == null) return;

        var zone = GetHomeBranchOwner(branch);
        if (zone != null)
        {
            try
            {
                if (!zone.IsPCFaction)
                    return;
            }
            catch { }
        }

        var key = GetHomeBranchKey(branch);
        if (seen.Add(key))
            homes.Add(branch);
    }
    private static int CompareHomeBranches(FactionBranch a, FactionBranch b)
    {
        var zoneA = GetHomeBranchOwner(a);
        var zoneB = GetHomeBranchOwner(b);
        var homeA = IsPlayerMainHome(zoneA);
        var homeB = IsPlayerMainHome(zoneB);
        if (homeA != homeB) return homeA ? -1 : 1;
        return GetHomeBranchKey(a).CompareTo(GetHomeBranchKey(b));
    }
    private static bool IsPlayerMainHome(Zone? zone)
    {
        try { return zone != null && ReferenceEquals(zone, GameAccess.Characters.PlayerCharacter?.homeZone); }
        catch { return false; }
    }
    private static int GetHomeBranchKey(FactionBranch? branch)
    {
        var zone = GetHomeBranchOwner(branch);
        if (zone != null)
        {
            try { return zone.uid; } catch { }
        }
        return branch == null ? 0 : branch.GetHashCode();
    }
    private static Zone? GetHomeBranchOwner(FactionBranch? branch)
    {
        try { return branch?.owner; }
        catch { return null; }
    }
    private static string GetHomeBranchDisplayName(FactionBranch branch)
    {
        var zone = GetHomeBranchOwner(branch);
        var marker = IsPlayerMainHome(zone) ? "★ " : "";
        return marker + SafeZoneNameWithLevel(zone) + " [" + GetHomeBranchKey(branch).ToString(CultureInfo.InvariantCulture) + "]";
    }
    private static string SafeZoneNameWithLevel(Zone? zone)
    {
        if (zone == null) return "???";
        try { return zone.NameWithLevel; }
        catch { return SafeZoneName(zone); }
    }
    private void EnsureHomeRows()
    {
        if (_homeSkillRows != null && _homeFeatRows != null && _homePolicyRows != null)
            return;

        _homeSkillRows = new List<HomeElementDef>();
        _homeFeatRows = new List<HomeElementDef>();
        _homePolicyRows = new List<HomeElementDef>();
        var seen = new HashSet<int>();

        foreach (var sourceRow in EnumerateSourceElementRows())
        {
            var id = GetInt(sourceRow, "id");
            if (id <= 0 || !seen.Add(id)) continue;

            var category = GetString(sourceRow, "category");
            if (!string.Equals(category, "tech", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(category, "landfeat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(category, "policy", StringComparison.OrdinalIgnoreCase))
                continue;

            var tags = GetStringArray(sourceRow, "tag");
            if (string.Equals(category, "policy", StringComparison.OrdinalIgnoreCase) && ContainsText(tags, "hidden"))
                continue;

            var name = GetElementDisplayName(sourceRow);
            if (string.IsNullOrEmpty(name) || name.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                continue;

            var def = new HomeElementDef(id, name, category, sourceRow)
            {
                Alias = GetString(sourceRow, "alias"),
                Max = GetInt(sourceRow, "max")
            };

            if (string.Equals(category, "tech", StringComparison.OrdinalIgnoreCase)) _homeSkillRows.Add(def);
            else if (string.Equals(category, "landfeat", StringComparison.OrdinalIgnoreCase)) _homeFeatRows.Add(def);
            else _homePolicyRows.Add(def);
        }

        MarkDuplicateHomeElementNames(_homeSkillRows);
        MarkDuplicateHomeElementNames(_homeFeatRows);
        MarkDuplicateHomeElementNames(_homePolicyRows);
        _homeSkillRows.Sort(CompareHomeElements);
        _homeFeatRows.Sort(CompareHomeElements);
        _homePolicyRows.Sort(CompareHomeElements);
        _homeLog = T("已读取家园数据：技能 ", "Loaded home data: skills ") + _homeSkillRows.Count +
                   T("，专长 ", ", feats ") + _homeFeatRows.Count +
                   T("，政策 ", ", policies ") + _homePolicyRows.Count;
    }
    private static void MarkDuplicateHomeElementNames(List<HomeElementDef> rows)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = row.Name.Trim();
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }
        foreach (var row in rows)
        {
            row.DisplayName = row.Name;
            if (counts.TryGetValue(row.Name.Trim(), out var count) && count > 1)
                row.DisplayName = row.Name + " [" + (string.IsNullOrEmpty(row.Alias) ? row.Id.ToString(CultureInfo.InvariantCulture) : row.Alias) + "]";
        }
    }
    private static int CompareHomeElements(HomeElementDef a, HomeElementDef b)
    {
        var sortA = GetInt(a.Source, "sort");
        var sortB = GetInt(b.Source, "sort");
        if (sortA != sortB) return sortA.CompareTo(sortB);
        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
    private static bool PassHomeElementFilter(HomeElementDef row, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var f = filter.ToLowerInvariant();
        return row.DisplayName.ToLowerInvariant().Contains(f) ||
               row.Name.ToLowerInvariant().Contains(f) ||
               row.Id.ToString(CultureInfo.InvariantCulture).Contains(f) ||
               (row.Alias ?? "").ToLowerInvariant().Contains(f) ||
               (row.Category ?? "").ToLowerInvariant().Contains(f);
    }
    private static bool ContainsText(string[] values, string text)
    {
        if (values == null) return false;
        for (var i = 0; i < values.Length; i++)
            if (string.Equals(values[i], text, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private static string GetHomeElementLabel(HomeElementDef row)
    {
        if (!string.IsNullOrEmpty(row.Alias))
            return row.DisplayName + " [" + row.Alias + "]";
        return row.DisplayName + " [" + row.Id.ToString(CultureInfo.InvariantCulture) + "]";
    }
    private static string GetHomeElementValueText(FactionBranch branch, int id)
    {
        try { return branch.Evalue(id).ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }
    private static int GetHomeElementBaseLevel(FactionBranch branch, int id)
    {
        try
        {
            var element = branch.elements?.GetElement(id);
            return element == null ? 0 : element.ValueWithoutLink;
        }
        catch { return 0; }
    }
    private void SetHomeElementLevel(FactionBranch branch, HomeElementDef row, int value, HomeElementKind kind)
    {
        try
        {
            value = Math.Max(0, value);
            SetHomeElementBase(branch, row.Id, value);
            if (kind == HomeElementKind.Policy && value > 0)
                EnsureHomePolicy(branch, row.Id, false);
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + GetHomeElementLabel(row) + " = " + value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            _homeLog = GetHomeElementLabel(row) + T(" 设置失败: ", " set failed: ") + ex.Message;
        }
    }
    private void SetHomePolicyActive(FactionBranch branch, HomeElementDef row, bool active)
    {
        try
        {
            EnsureHomePolicy(branch, row.Id, false);
            branch.policies.SetActive(row.Id, active);
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + GetHomeElementLabel(row) + " " + (active ? T("政策启用", "Active") : T("政策关闭", "Inactive"));
        }
        catch (Exception ex)
        {
            _homeLog = GetHomeElementLabel(row) + T(" 设置失败: ", " set failed: ") + ex.Message;
        }
    }
    private static void EnsureHomePolicy(FactionBranch branch, int id, bool active)
    {
        if (branch.policies == null) return;
        if (!branch.policies.HasPolicy(id))
            branch.policies.AddPolicy(id, false);
        if (active)
            branch.policies.SetActive(id, true);
    }
    private static bool IsHomePolicyActive(FactionBranch branch, int id)
    {
        try { return branch.policies != null && branch.policies.IsActive(id, -1); }
        catch { return false; }
    }
    private static void SetHomeElementBase(FactionBranch branch, int id, int value)
    {
        branch.elements.SetBase(id, Math.Max(0, value), 0);
    }
    private void SetHomeBranchLevel(FactionBranch branch, int value)
    {
        try
        {
            branch.lv = Math.Max(1, value);
            try { branch.ValidateUpgradePolicies(); } catch { }
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + T("盟约之石等级", "Covenant Stone Level") + " = " + branch.lv.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("盟约之石等级", "Covenant Stone Level") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeCivility(FactionBranch branch, int value)
    {
        try
        {
            var current = branch.GetCivility();
            var nextBase = GetHomeElementBaseLevel(branch, 2203) + value - current;
            SetHomeElementBase(branch, 2203, Math.Max(0, nextBase));
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + T("居民素质", "Resident Civility") + " = " + value.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("居民素质", "Resident Civility") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeFertility(FactionBranch branch, Zone zone, int value)
    {
        try
        {
            var map = GetHomeMap(zone);
            if (map == null || map.bounds == null)
            {
                _homeLog = T("肥沃度", "Fertility") + T(" 设置失败: ", " set failed: ") + "map not loaded";
                return;
            }

            var baseSoil = (int)(Math.Sqrt(Math.Max(1, map.bounds.Width * map.bounds.Height)) * 3);
            var modifier = 100 + Math.Max(0, branch.Evalue(3700)) * 25;
            var targetRaw = (int)Math.Ceiling(Math.Max(0, value) * 100.0 / Math.Max(1, modifier));
            var targetTotal = (int)Math.Ceiling((targetRaw - baseSoil) / 5.0);
            var currentTotal = branch.Evalue(2200);
            var currentBase = GetHomeElementBaseLevel(branch, 2200);
            var inferredShared = currentTotal - currentBase;
            SetHomeElementBase(branch, 2200, Math.Max(0, targetTotal - inferredShared));
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + T("肥沃度", "Fertility") + " = " + branch.MaxSoil.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("肥沃度", "Fertility") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeDevelopment(Zone zone, int value)
    {
        try
        {
            if (zone == null)
            {
                _homeLog = T("发展度", "Development") + T(" 设置失败: ", " set failed: ") + "zone not found";
                return;
            }
            zone.development = Math.Max(0, value) * 10;
            _homeLog = T("已设置家园", "Set home") + ": " + T("发展度", "Development") + " = " + Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("发展度", "Development") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeDanger(FactionBranch branch, int value)
    {
        try
        {
            var target = Math.Max(1, value);
            var content = Math.Max(1, branch.ContentLV);
            var requiredReduction = Math.Max(0, content - target);
            var policyLevel = 0;
            if (requiredReduction > 0)
            {
                var sqrt = (int)Math.Ceiling(requiredReduction / 2.0);
                policyLevel = sqrt * sqrt;
            }
            SetHomeElementBase(branch, 2704, policyLevel);
            EnsureHomePolicy(branch, 2704, true);
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + T("危险度", "Danger Level") + " = " + branch.DangerLV.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("危险度", "Danger Level") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeMaxAp(FactionBranch branch, int value)
    {
        try
        {
            var nextBase = GetHomeElementBaseLevel(branch, 2115) + value - branch.MaxAP;
            SetHomeElementBase(branch, 2115, Math.Max(0, nextBase));
            RefreshHomeSystems(branch);
            _homeLog = T("已设置家园", "Set home") + ": " + T("运营力上限", "Max Admin Power") + " = " + branch.MaxAP.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { _homeLog = T("运营力上限", "Max Admin Power") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private void SetHomeLandArea(Zone zone, int value)
    {
        try
        {
            var map = GetHomeMap(zone);
            if (map == null || map.bounds == null)
            {
                _homeLog = T("土地", "Land") + T(" 设置失败: ", " set failed: ") + "map not loaded";
                return;
            }

            var bounds = map.bounds;
            var maxSize = Math.Max(1, map.Size - 2);
            var targetTiles = Math.Max(1, value) * 100;
            var aspect = bounds.Height <= 0 ? 1.0 : bounds.Width / (double)bounds.Height;
            var width = Clamp((int)Math.Round(Math.Sqrt(targetTiles * aspect)), 1, maxSize);
            var height = Clamp((int)Math.Ceiling(targetTiles / (double)Math.Max(1, width)), 1, maxSize);
            if (width * height > targetTiles + Math.Max(width, height))
            {
                var adjustedWidth = Clamp((int)Math.Ceiling(targetTiles / (double)Math.Max(1, height)), 1, maxSize);
                width = adjustedWidth;
            }

            var centerX = bounds.CenterX;
            var centerZ = bounds.CenterZ;
            var x = Clamp(centerX - width / 2, 1, Math.Max(1, maxSize - width + 1));
            var z = Clamp(centerZ - height / 2, 1, Math.Max(1, maxSize - height + 1));
            bounds.SetBounds(x, z, x + width - 1, z + height - 1);
            _homeLog = T("已设置家园", "Set home") + ": " + T("土地", "Land") + " = " + GetHomeLandAreaText(zone);
        }
        catch (Exception ex) { _homeLog = T("土地", "Land") + T(" 设置失败: ", " set failed: ") + ex.Message; }
    }
    private static void RefreshHomeSystems(FactionBranch branch)
    {
        try { branch.policies?.RefreshEffects(); } catch { }
        try { branch.resources?.SetDirty(); } catch { }
        try { branch.resources?.Refresh(); } catch { }
        try { branch.RefreshEfficiency(); } catch { }
    }
    private static string SafeHomeInt(Func<int> getter)
    {
        try { return getter().ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }
    private static Zone GetHomeZone(FactionBranch branch)
    {
        try
        {
            if (branch != null && branch.owner != null)
                return branch.owner;
        }
        catch { }
        try
        {
            if (GameAccess.Characters.PlayerCharacter != null && GameAccess.Characters.PlayerCharacter.homeZone != null)
                return GameAccess.Characters.PlayerCharacter.homeZone;
        }
        catch { }
        try { return GameAccess.World.CurrentZone; }
        catch { return null; }
    }
    private static Map GetHomeMap(Zone zone)
    {
        try
        {
            if (zone != null && zone.map != null)
                return zone.map;
        }
        catch { }
        try
        {
            if (zone == null || ReferenceEquals(zone, GameAccess.World.CurrentZone))
                return GameAccess.World.CurrentMap;
        }
        catch { }
        return null;
    }
    private static string SafeZoneName(Zone zone)
    {
        if (zone == null) return "???";
        try { return zone.Name; }
        catch { return zone.ToString(); }
    }
    private static string GetHomeLandAreaText(Zone zone)
    {
        try
        {
            var map = GetHomeMap(zone);
            if (map == null || map.bounds == null) return "?";
            return (map.bounds.Width * map.bounds.Height / 100).ToString(CultureInfo.InvariantCulture);
        }
        catch { return "?"; }
    }
}
