using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class ProbabilityModule
{
    private void ScanProbabilityEntries(bool userRequested)
    {
        var currentSourceManager = GetCurrentProbabilitySourceManager();
        var preserveState = userRequested &&
                                   _probabilityScanned &&
                                   ReferenceEquals(_probabilitySourceManager, currentSourceManager);
        var preservedStates = preserveState ? CaptureProbabilityEntryStates() : null;

        if (!preserveState && _probabilityModifiedCount > 0)
        {
            RestoreAll(false);
            if (_probabilityModifiedCount > 0)
            {
                _probabilityLog = T("仍有概率项恢复失败，已取消重新扫描", "Some probability values could not be restored; rescan was cancelled");
                return;
            }
        }
        _probabilityEntries.Clear();
        _probabilityRows.Clear();
        _probabilityModifiedCount = 0;
        _probabilityScanned = true;
        _probabilitySourceManager = currentSourceManager;

        if (_probabilitySourceManager == null || !HasCharacterData())
        {
            _probabilityLog = T("未获取到当前游戏数据", "Current game data is unavailable");
            return;
        }

        var seenRows = new HashSet<object>(ProbabilityReferenceComparer.Instance);
        var sourceCount = 0;
        var errorCount = 0;
        try
        {
            var sourceFields = _probabilitySourceManager.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(sourceFields, (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            for (var i = 0; i < sourceFields.Length; i++)
            {
                var sourceField = sourceFields[i];
                if (string.Equals(sourceField.Name, "cards", StringComparison.OrdinalIgnoreCase) ||
                    !sourceField.FieldType.Name.StartsWith("Source", StringComparison.Ordinal))
                    continue;

                object? source;
                try { source = sourceField.GetValue(_probabilitySourceManager); }
                catch { errorCount++; continue; }
                if (source == null)
                    continue;

                var foundSourceRow = false;
                foreach (var row in EnumerateProbabilitySourceRows(source))
                {
                    if (row == null || !seenRows.Add(row))
                        continue;
                    foundSourceRow = true;
                    AddProbabilityMembers(sourceField.Name, row, ref errorCount);
                }
                if (foundSourceRow)
                    sourceCount++;
            }
        }
        catch
        {
            errorCount++;
        }

        if (!preserveState)
        {
            CaptureMiniGameProbabilityDefaults();
            CaptureDropMultiplierDefaults();
        }
        AddMiniGameProbabilityEntries(ref errorCount);
        AddDropMultiplierEntries(ref errorCount);
        EnsureSlotProbabilityPatch();

        if (preservedStates != null)
            RestoreProbabilityEntryStates(preservedStates);

        _probabilityEntries.Sort((left, right) =>
        {
            var result = GetProbabilityCategoryIndex(left.CategoryKey).CompareTo(GetProbabilityCategoryIndex(right.CategoryKey));
            if (result != 0) return result;
            result = string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            if (result != 0) return result;
            result = string.Compare(left.RowId, right.RowId, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
            return string.Compare(left.Field.Name, right.Field.Name, StringComparison.OrdinalIgnoreCase);
        });

        _probabilityLog = T("已扫描 ", "Scanned ") + sourceCount.ToString(CultureInfo.InvariantCulture) +
                                 T(" 类数据源，发现 ", " source groups and found ") + _probabilityEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                 T(" 项可修改概率", " editable probability values") +
                                 (errorCount > 0 ? T("，跳过异常 ", "; skipped errors: ") + errorCount.ToString(CultureInfo.InvariantCulture) : "");
        if (!userRequested && _probabilityEntries.Count == 0)
            _probabilityLog = T("未发现可修改的概率字段", "No editable probability fields were found");
    }
    private Dictionary<string, Queue<ProbabilityEntryState>> CaptureProbabilityEntryStates()
    {
        var result = new Dictionary<string, Queue<ProbabilityEntryState>>(StringComparer.Ordinal);
        for (var i = 0; i < _probabilityEntries.Count; i++)
        {
            var entry = _probabilityEntries[i];
            if (!entry.HasOriginal && !entry.InputDirty)
                continue;

            object current;
            try { current = entry.ReadCurrent(); }
            catch { current = entry.InitialValue; }

            var key = GetProbabilityEntryStateKey(entry);
            if (!result.TryGetValue(key, out var states))
            {
                states = new Queue<ProbabilityEntryState>();
                result[key] = states;
            }
            states.Enqueue(new ProbabilityEntryState(entry, current));
        }
        return result;
    }
    private void RestoreProbabilityEntryStates(Dictionary<string, Queue<ProbabilityEntryState>> states)
    {
        var restoredValues = false;
        for (var i = 0; i < _probabilityEntries.Count; i++)
        {
            var entry = _probabilityEntries[i];
            var key = GetProbabilityEntryStateKey(entry);
            if (!states.TryGetValue(key, out var matches) || matches.Count == 0)
                continue;

            var state = matches.Dequeue();
            entry.InitialValue = state.InitialValue;
            entry.OriginalValue = state.OriginalValue;
            entry.HasOriginal = state.HasOriginal;
            entry.InputText = state.InputText;
            entry.InputDirty = state.InputDirty;
            if (!state.HasOriginal)
                continue;

            try
            {
                entry.Field.SetValue(entry.Owner, state.CurrentValue);
                restoredValues = true;
            }
            catch
            {
                entry.HasOriginal = false;
                entry.OriginalValue = null;
                entry.InputDirty = false;
                try { entry.InputText = FormatProbabilityValue(entry.ReadCurrent()); }
                catch { entry.InputText = FormatProbabilityValue(entry.InitialValue); }
            }
        }

        if (restoredValues)
            RefreshProbabilityCaches();
        RecountProbabilityModifications();
    }
    private static string GetProbabilityEntryStateKey(ProbabilityEntry entry)
    {
        return entry.PersistentId;
    }
    private string CaptureStoredConfigurationJson()
    {
        var entries = new JArray();
        for (var i = 0; i < _probabilityEntries.Count; i++)
        {
            var entry = _probabilityEntries[i];
            if (!entry.HasOriginal)
                continue;

            try
            {
                entries.Add(new JObject
                {
                    ["id"] = entry.PersistentId,
                    ["value"] = FormatProbabilityValue(entry.ReadCurrent())
                });
            }
            catch
            {
            }
        }

        return new JObject
        {
            ["version"] = 1,
            ["entries"] = entries
        }.ToString(Formatting.None);
    }
    private void ApplyStoredConfigurationJson(string json)
    {
        RestoreAll(false);

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            _probabilityLog = T("读取模块配置失败: ", "Failed to load module config: ") + ex.Message;
            return;
        }

        var savedEntries = root["entries"] as JArray;
        if (savedEntries == null || savedEntries.Count == 0)
        {
            _probabilityLog = T("事件概率模块配置已读取，共 0 项修改", "Event probability module config loaded: 0 modifications");
            return;
        }

        var available = new Dictionary<string, Queue<ProbabilityEntry>>(StringComparer.Ordinal);
        for (var i = 0; i < _probabilityEntries.Count; i++)
        {
            var entry = _probabilityEntries[i];
            var key = entry.PersistentId;
            Queue<ProbabilityEntry>? queue;
            if (!available.TryGetValue(key, out queue))
            {
                queue = new Queue<ProbabilityEntry>();
                available[key] = queue;
            }
            queue.Enqueue(entry);
        }

        var applied = 0;
        var skipped = 0;
        var changed = false;
        for (var i = 0; i < savedEntries.Count; i++)
        {
            var saved = savedEntries[i] as JObject;
            var key = saved?["id"]?.Value<string>() ?? "";
            if (key.Length == 0)
                key = ResolveLegacyStoredEntryId(saved?["key"]?.Value<string>() ?? "");
            var valueText = saved?["value"]?.Value<string>() ?? "";
            Queue<ProbabilityEntry>? matches;
            if (key.Length == 0 || !available.TryGetValue(key, out matches) || matches.Count == 0)
            {
                skipped++;
                continue;
            }

            var entry = matches.Dequeue();
            object parsed;
            string parseError;
            if (!TryParseProbabilityValue(valueText, entry.Field.FieldType, out parsed, out parseError))
            {
                skipped++;
                continue;
            }

            try
            {
                var current = entry.ReadCurrent();
                entry.InputText = FormatProbabilityValue(parsed);
                entry.InputDirty = false;
                if (Equals(current, parsed))
                    continue;

                entry.OriginalValue = current;
                entry.HasOriginal = true;
                entry.Field.SetValue(entry.Owner, parsed);
                entry.InputText = FormatProbabilityValue(entry.ReadCurrent());
                changed = true;
                applied++;
            }
            catch
            {
                entry.HasOriginal = false;
                entry.OriginalValue = null;
                skipped++;
            }
        }

        if (changed)
            RefreshProbabilityCaches();
        RecountProbabilityModifications();
        _probabilityLog = T("事件概率模块配置已读取，共 ", "Event probability module config loaded: ") +
                          applied.ToString(CultureInfo.InvariantCulture) +
                          T(" 项修改", " modifications") +
                          (skipped > 0
                              ? T("，跳过 ", "; skipped ") + skipped.ToString(CultureInfo.InvariantCulture)
                              : "");
    }
    private static string NormalizeStoredConfigurationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return EmptyStoredConfigurationJson;

        try
        {
            var root = JObject.Parse(json);
            var entries = root["entries"] as JArray;
            if (entries == null)
                return EmptyStoredConfigurationJson;
            var version = root["version"]?.Value<int?>() ?? 1;
            return new JObject
            {
                ["version"] = version >= 2 ? 2 : 1,
                ["entries"] = entries.DeepClone()
            }.ToString(Formatting.None);
        }
        catch (JsonException)
        {
            return EmptyStoredConfigurationJson;
        }
    }
    private string ResolveLegacyStoredEntryId(string legacyKey)
    {
        if (string.IsNullOrWhiteSpace(legacyKey))
            return "";

        var parts = legacyKey.Split('\u001f');
        if (parts.Length < 6)
            return "";

        var category = parts[0];
        var sourceName = parts[1];
        var rowId = parts[2];
        var memberName = parts[4];
        var displayName = string.Join("\u001f", parts, 5, parts.Length - 5);

        if (string.Equals(sourceName, "MiniGame", StringComparison.Ordinal) ||
            string.Equals(sourceName, "DropMultiplier", StringComparison.Ordinal))
        {
            for (var i = 0; i < _probabilityEntries.Count; i++)
            {
                var entry = _probabilityEntries[i];
                if (string.Equals(entry.SourceName, sourceName, StringComparison.Ordinal) &&
                    string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))
                    return entry.PersistentId;
            }

            return ResolveLegacyManualProbabilityId(displayName);
        }

        return BuildSourcePersistentId(category, sourceName, rowId, displayName, memberName);
    }
    private static string ResolveLegacyManualProbabilityId(string displayName)
    {
        switch (displayName)
        {
            case "老虎机：额外强制中奖":
            case "Slot machine: extra forced win":
            case "スロット：追加強制当選":
            case "Игровой автомат: дополнительный гарантированный выигрыш":
                return "minigame.slot_extra_forced_win";
            case "刮刮乐：奖章奖励":
            case "Scratch card: medal reward":
            case "スクラッチ：メダル報酬":
            case "Скретч-карта: награда медалью":
                return "minigame.scratch_medal_reward";
            case "刮刮乐：白金币奖励":
            case "Scratch card: platinum coin reward":
            case "スクラッチ：プラチナ硬貨報酬":
            case "Скретч-карта: награда платиновой монетой":
                return "minigame.scratch_platinum_reward";
            case "刮刮乐：家具奖励":
            case "Scratch card: furniture reward":
            case "スクラッチ：家具報酬":
            case "Скретч-карта: награда мебелью":
                return "minigame.scratch_furniture_reward";
            case "刮刮乐：塑像盒奖励":
            case "Scratch card: model box reward":
            case "スクラッチ：プラモボックス報酬":
            case "Скретч-карта: награда коробкой модели":
                return "minigame.scratch_model_box_reward";
            case "刮刮乐：食物奖励":
            case "Scratch card: food reward":
            case "スクラッチ：食料報酬":
            case "Скретч-карта: награда едой":
                return "minigame.scratch_food_reward";
            case "幸运转盘：一等奖":
            case "Fortune roll: first prize":
            case "幸運くじ：1等":
            case "Колесо удачи: первый приз":
                return "minigame.fortune_first_prize";
            case "幸运转盘：二等奖":
            case "Fortune roll: second prize":
            case "幸運くじ：2等":
            case "Колесо удачи: второй приз":
                return "minigame.fortune_second_prize";
            case "幸运转盘：三等奖":
            case "Fortune roll: third prize":
            case "幸運くじ：3等":
            case "Колесо удачи: третий приз":
                return "minigame.fortune_third_prize";
            case "赌博宝箱：强制成功":
            case "Gamble chest: forced success":
            case "ギャンブル宝箱：強制成功":
            case "Азартный сундук: принудительный успех":
                return "minigame.gamble_chest_forced_success";
            case "赌博宝箱：强制失败":
            case "Gamble chest: forced failure":
            case "ギャンブル宝箱：強制失敗":
            case "Азартный сундук: принудительная неудача":
                return "minigame.gamble_chest_forced_failure";
            case "赌博宝箱：大奖随机范围":
            case "Gamble chest: jackpot random range":
            case "ギャンブル宝箱：大当たり乱数範囲":
            case "Азартный сундук: случайный диапазон джекпота":
                return "minigame.gamble_chest_jackpot_range";
            case "品质倍率":
            case "Quality multiplier":
                return "drop.quality_multiplier";
            case "数量倍率":
            case "Quantity multiplier":
                return "drop.quantity_multiplier";
            default:
                return "";
        }
    }
    private static string BuildSourcePersistentId(
        string category,
        string sourceName,
        string rowId,
        string displayName,
        string memberName)
    {
        var rowIdentity = string.IsNullOrWhiteSpace(rowId)
            ? "name-" + (displayName ?? "")
            : "id-" + rowId;
        return "source/" +
               EscapePersistentIdSegment(category) + "/" +
               EscapePersistentIdSegment(sourceName) + "/" +
               EscapePersistentIdSegment(rowIdentity) + "/" +
               EscapePersistentIdSegment(memberName);
    }
    private static string EscapePersistentIdSegment(string? value)
    {
        try
        {
            return Uri.EscapeDataString(value ?? "");
        }
        catch
        {
            return (value ?? "").Replace("%", "%25").Replace("/", "%2F");
        }
    }
}
