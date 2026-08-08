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
    internal const string EmptyStoredConfigurationJson = "{\"version\":2,\"entries\":[]}";
    private readonly ElinModifierPlugin _host;
    internal enum ProbabilityMemberLabelKind
    {
        Auto,
        Percent,
        Denominator,
        RandomRange,
        Multiplier
    }
    private sealed class MiniGameProbabilityState
    {
        public int SlotForcedWinPercent;
        public int ScratchMedalDenominator = 20;
        public int ScratchPlatinumDenominator = 10;
        public int ScratchFurnitureDenominator = 10;
        public int ScratchModelBoxDenominator = 4;
        public int ScratchFoodDenominator = 4;
        public int FortuneGrade1Denominator = 8;
        public int FortuneGrade2Denominator = 25;
        public int FortuneGrade3Denominator = 60;
        public int GambleChestForcedSuccessDenominator = 20;
        public int GambleChestForcedFailureDenominator = 20;
        public int GambleChestJackpotRange = 10000;
    }
    private sealed class DropMultiplierState
    {
        public float QualityMultiplier = 1f;
        public float QuantityMultiplier = 1f;
    }
    private struct DropZoneAddState
    {
        public Thing? Source;
        public int ExtraCopies;
        public bool Skip;
    }
    internal sealed class ProbabilityEntry
    {
        public readonly string CategoryKey;
        public readonly string SourceName;
        public readonly string RowId;
        public readonly string DisplayName;
        public readonly string PersistentId;
        public readonly object Owner;
        public readonly FieldInfo Field;
        public readonly ProbabilityMemberLabelKind MemberLabelKind;
        public object InitialValue;
        public object? OriginalValue;
        public bool HasOriginal;
        public string InputText;
        public bool InputDirty;

        public ProbabilityEntry(
            string categoryKey,
            string sourceName,
            string rowId,
            string displayName,
            object owner,
            FieldInfo field,
            object initialValue,
            ProbabilityMemberLabelKind memberLabelKind = ProbabilityMemberLabelKind.Auto,
            string persistentId = "")
        {
            CategoryKey = categoryKey;
            SourceName = sourceName;
            RowId = rowId;
            DisplayName = displayName;
            PersistentId = persistentId;
            Owner = owner;
            Field = field;
            MemberLabelKind = memberLabelKind;
            InitialValue = initialValue;
            InputText = FormatProbabilityValue(initialValue);
        }

        public object ReadCurrent()
        {
            return Field.GetValue(Owner) ?? InitialValue;
        }
    }
    private sealed class ProbabilityEntryState
    {
        public readonly object InitialValue;
        public readonly object? OriginalValue;
        public readonly object CurrentValue;
        public readonly bool HasOriginal;
        public readonly string InputText;
        public readonly bool InputDirty;

        public ProbabilityEntryState(ProbabilityEntry entry, object currentValue)
        {
            InitialValue = entry.InitialValue;
            OriginalValue = entry.OriginalValue;
            CurrentValue = currentValue;
            HasOriginal = entry.HasOriginal;
            InputText = entry.InputText;
            InputDirty = entry.InputDirty;
        }
    }
    internal sealed class ProbabilityRow
    {
        public readonly string CategoryKey;
        public readonly ProbabilityEntry? Entry;
        public readonly int Count;
        public readonly int ModifiedCount;
        public readonly bool Expanded;
        public readonly string Message;

        public bool IsHeader => Entry == null && CategoryKey.Length > 0;
        public bool IsMessage => Entry == null && CategoryKey.Length == 0;

        public ProbabilityRow(string categoryKey, int count, int modifiedCount, bool expanded)
        {
            CategoryKey = categoryKey;
            Count = count;
            ModifiedCount = modifiedCount;
            Expanded = expanded;
            Message = "";
        }

        public ProbabilityRow(ProbabilityEntry entry)
        {
            CategoryKey = entry.CategoryKey;
            Entry = entry;
            Message = "";
        }

        public ProbabilityRow(string message)
        {
            CategoryKey = "";
            Message = message ?? "";
        }
    }
    private sealed class ProbabilityReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ProbabilityReferenceComparer Instance = new ProbabilityReferenceComparer();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
    private static readonly string[] ProbabilityCategoryOrder =
    {
        "character",
        "item",
        "food",
        "material",
        "element",
        "zone",
        "quest",
        "minigame",
        "drop",
        "other"
    };
    private VirtualList<ProbabilityRow>? _probabilityList;
    private readonly List<ProbabilityEntry> _probabilityEntries = new List<ProbabilityEntry>();
    private readonly List<ProbabilityRow> _probabilityRows = new List<ProbabilityRow>();
    private readonly Dictionary<string, bool> _probabilityCategoryExpanded = new Dictionary<string, bool>(StringComparer.Ordinal);
    private object? _probabilitySourceManager;
    private Text? _probabilitySummaryText;
    private string _probabilityFilter = "";
    private string _probabilityLog = "";
    private bool _probabilityScanned;
    private bool _probabilityFilterDirty;
    private float _probabilityFilterDueAt;
    private int _probabilityModifiedCount;
    private string _storedConfigurationJson = EmptyStoredConfigurationJson;
    private readonly MiniGameProbabilityState _miniGameProbability = new MiniGameProbabilityState();
    private readonly DropMultiplierState _dropMultiplier = new DropMultiplierState();
    [ThreadStatic] private static int _dropMultiplierDepth;
    [ThreadStatic] private static bool _dropMultiplierReplaying;
    private bool _slotProbabilityPatchInstalled;
    private bool _gambleChestProbabilityPatchInstalled;
    private int _slotProbabilityPatchRetryFrame;
    internal ProbabilityModule(ElinModifierPlugin host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }
    internal string Log => _probabilityLog;
    internal string StoredConfigurationJson => _storedConfigurationJson;
    private RectTransform? _lGuiPageHost => _host.ModuleLGuiPageHost;
    private Harmony? _harmony => _host.ModuleHarmony;
    private string T(string zh, string en) => _host.TranslateModuleText(zh, en);
    private bool HasCharacterData() => ElinModifierPlugin.HasModuleCharacterData();
    private bool IsProbabilityPageActive() => _host.IsModuleProbabilityPageActive();
    private RectTransform CreateLGuiRect(Transform parent, string name) => _host.CreateModuleLGuiRect(parent, name);
    private InputField CreateLGuiInput(
        Transform parent,
        string name,
        string placeholder,
        float x,
        float y,
        float width,
        float height) =>
        _host.CreateModuleLGuiInput(parent, name, placeholder, x, y, width, height);
    private Button CreateLGuiButton(
        Transform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action? action) =>
        _host.CreateModuleLGuiButton(parent, name, label, x, y, width, height, action);
    private Text CreateLGuiText(
        Transform parent,
        string name,
        string value,
        int size,
        TextAnchor anchor,
        FontStyle style) =>
        _host.CreateModuleLGuiText(parent, name, value, size, anchor, style);
    private ScrollRect CreateLGuiScroll(RectTransform parent, string name, float top) =>
        _host.CreateModuleLGuiScroll(parent, name, top);
    private RectTransform CreateLGuiVirtualRow(RectTransform parent) => _host.CreateModuleLGuiVirtualRow(parent);
    private void ApplyLGuiRowVisual(LGuiRowView view, int index, bool header = false) =>
        _host.ApplyModuleLGuiRowVisual(view, index, header);
    private static void AnchorLGuiTop(RectTransform rect, float top, float height, float left, float right) =>
        ElinModifierPlugin.AnchorModuleLGuiTop(rect, top, height, left, right);
    private static void PlaceLGuiRect(RectTransform rect, float x, float y, float width, float height) =>
        ElinModifierPlugin.PlaceModuleLGuiRect(rect, x, y, width, height);
    private static string IndentLGuiText(string text, int depth) => ElinModifierPlugin.IndentModuleLGuiText(text, depth);
    private static bool LGuiFilterMatches(string first, string second, string third, string filter) =>
        ElinModifierPlugin.ModuleLGuiFilterMatches(first, second, third, filter);
    internal void RefreshVisibleRows() => _probabilityList?.RefreshBoundRows();
    internal void SetStoredConfigurationJson(string? json)
    {
        _storedConfigurationJson = NormalizeStoredConfigurationJson(json);
    }
    internal void ResetStoredConfiguration()
    {
        _storedConfigurationJson = EmptyStoredConfigurationJson;
    }
    internal void DisposeUi()
    {
        _probabilityList?.Dispose();
        _probabilityList = null;
        _probabilitySummaryText = null;
    }
    internal void ToggleCategory(ProbabilityRow row)
    {
        _probabilityCategoryExpanded[row.CategoryKey] = !row.Expanded;
        RebuildProbabilityRows();
    }
    internal void ApplyRow(ProbabilityRow row, string value)
    {
        if (row.Entry == null)
            return;
        row.Entry.InputText = value ?? "";
        ApplyProbabilityEntry(row.Entry, row.Entry.InputText);
        RebuildProbabilityRows();
    }
    internal void RestoreRow(ProbabilityRow row)
    {
        if (row.Entry == null)
            return;
        RestoreProbabilityEntry(row.Entry, true);
        RebuildProbabilityRows();
    }
    internal void SetRowInput(ProbabilityRow row, string value)
    {
        if (row.Entry == null)
            return;
        row.Entry.InputText = value ?? "";
        row.Entry.InputDirty = true;
    }
}
