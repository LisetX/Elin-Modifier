using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

internal sealed class PcGeneAbilityProjection
{
    private sealed class Candidate
    {
        internal Candidate(SourceElement.Row source, bool negative)
        {
            Source = source;
            Count = 1;
            if (negative)
                NegativeCount = 1;
            else
                PositiveCount = 1;
        }

        internal SourceElement.Row Source { get; }
        internal int Count { get; set; }
        internal int PositiveCount { get; private set; }
        internal int NegativeCount { get; private set; }

        internal void Increment(bool negative)
        {
            Count++;
            if (negative)
                NegativeCount++;
            else
                PositiveCount++;
        }
    }

    private sealed class ProjectedAbility
    {
        internal ProjectedAbility(
            int id,
            string alias,
            string categorySub,
            SourceElement.Row source,
            Act act,
            int geneCount)
        {
            Id = id;
            Alias = alias;
            CategorySub = categorySub;
            Source = source;
            Act = act;
            GeneCount = geneCount;
        }

        internal int Id { get; }
        internal string Alias { get; }
        internal string CategorySub { get; }
        internal SourceElement.Row Source { get; }
        internal Act Act { get; }
        internal int GeneCount { get; set; }
        internal bool NativeElementActive { get; set; }
    }

    private readonly IGameRuntimeContext _runtime;
    private readonly IGameSourceRepository _sources;
    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameValue<CharaGenes> _characterGenes;
    private readonly IBoundGameValue<List<DNA>> _installedGenes;
    private readonly IBoundGameValue<DNA.Type> _geneType;
    private readonly IBoundGameValue<List<int>> _geneValues;
    private readonly IBoundGameValue<Dictionary<int, SourceElement.Row>> _sourceRows;
    private readonly IBoundGameValue<string> _sourceCategory;
    private readonly IBoundGameValue<string> _sourceCategorySub;
    private readonly IBoundGameValue<string> _sourceAlias;
    private readonly IBoundGameMethod _createElement;
    private readonly IBoundGameValue<ElementContainer> _elementOwner;
    private readonly IBoundGameValue<int> _elementValue;
    private readonly IBoundGameValue<int> _elementId;
    private readonly IBoundGameValue<int> _elementBase;
    private readonly IBoundGameValue<int> _elementSourceValue;
    private readonly IBoundGameValue<int> _elementLink;
    private readonly IBoundGameMethod _getElement;
    private readonly IBoundGameMethod _modBase;
    private readonly IBoundGameMethod _getSortValue;
    private readonly IBoundGameValue<CharaAbility> _characterAbility;
    private readonly IBoundGameValue<List<int>> _storedAbilities;
    private readonly IBoundGameValue<ActList> _abilityList;
    private readonly IBoundGameValue<List<ActList.Item>> _abilityItems;
    private readonly IBoundGameValue<Act> _abilityItemAct;
    private readonly IBoundGameMethod _refreshAbility;
    private readonly IBoundGameValue<SourceChara.Row> _characterSource;
    private readonly IBoundGameValue<string[]> _sourceCombatActs;
    private readonly IBoundGameMethod _useAbility;
    private readonly IBoundGameValue<UIDynamicList> _layerList;
    private readonly IBoundGameValue<Chara> _layerCharacter;
    private readonly IBoundGameValue<string[]> _layerGroups;
    private readonly IBoundGameValue<HashSet<int>> _favoriteAbilities;
    private readonly IBoundGameValue<Act> _buttonAct;
    private readonly IBoundGameValue<UIText> _buttonStockText;
    private readonly IBoundGameMethod _addToList;
    private readonly IBoundGameMethod _selectGroup;
    private readonly IBoundGameValue<LayerAbility>? _callbackLayer;
    private readonly IBoundGameValue<string>? _callbackGroup;
    private readonly Dictionary<int, ProjectedAbility> _abilities =
        new Dictionary<int, ProjectedAbility>();
    private Chara? _projectedCharacter;
    private ElementContainer? _projectedElements;
    private LayerAbility? _selectedLayer;
    private string? _selectedGroup;
    private Chara? _legacyCleanupCharacter;
    private readonly HashSet<int> _legacyCleanedIds = new HashSet<int>();
    private bool _redrawPending;

    internal PcGeneAbilityProjection(
        IGameRuntimeContext runtime,
        IGameSourceRepository sources,
        ICharacterGameAccess characters,
        IGameMemberBinder binder)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _characterGenes = binder.BindInstanceValue<CharaGenes>(
            typeof(Chara),
            GameValueAccess.Read,
            "c_genes");
        _installedGenes = binder.BindInstanceValue<List<DNA>>(
            typeof(CharaGenes),
            GameValueAccess.Read,
            "items");
        _geneType = binder.BindInstanceValue<DNA.Type>(
            typeof(DNA),
            GameValueAccess.Read,
            "type");
        _geneValues = binder.BindInstanceValue<List<int>>(
            typeof(DNA),
            GameValueAccess.Read,
            "vals");
        _sourceRows = binder.BindInstanceValue<Dictionary<int, SourceElement.Row>>(
            typeof(SourceElement),
            GameValueAccess.Read,
            "map");
        _sourceCategory = binder.BindInstanceValue<string>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "category");
        _sourceCategorySub = binder.BindInstanceValue<string>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "categorySub");
        _sourceAlias = binder.BindInstanceValue<string>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "alias");
        _createElement = binder.BindStaticMethod(
            typeof(Element),
            typeof(Element),
            new[] { typeof(int), typeof(int) },
            "Create");
        _elementOwner = binder.BindInstanceValue<ElementContainer>(
            typeof(Element),
            GameValueAccess.ReadWrite,
            "owner");
        _elementValue = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "Value");
        _elementId = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "id");
        _elementBase = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "vBase");
        _elementSourceValue = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "vSource");
        _elementLink = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "vLink");
        _getElement = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int) },
            "GetElement");
        _modBase = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int), typeof(int) },
            "ModBase");
        _getSortValue = binder.BindInstanceMethod(
            typeof(Element),
            typeof(int),
            new[] { typeof(UIList.SortMode) },
            "GetSortVal");
        _characterAbility = binder.BindInstanceValue<CharaAbility>(
            typeof(Chara),
            GameValueAccess.Read,
            "ability");
        _storedAbilities = binder.BindInstanceValue<List<int>>(
            typeof(Chara),
            GameValueAccess.ReadWrite,
            "_listAbility");
        _abilityList = binder.BindInstanceValue<ActList>(
            typeof(CharaAbility),
            GameValueAccess.Read,
            "list");
        _abilityItems = binder.BindInstanceValue<List<ActList.Item>>(
            typeof(ActList),
            GameValueAccess.Read,
            "items");
        _abilityItemAct = binder.BindInstanceValue<Act>(
            typeof(ActList.Item),
            GameValueAccess.Read,
            "act");
        _refreshAbility = binder.BindInstanceMethod(
            typeof(CharaAbility),
            typeof(void),
            Type.EmptyTypes,
            "Refresh");
        _characterSource = binder.BindInstanceValue<SourceChara.Row>(
            typeof(Chara),
            GameValueAccess.Read,
            "source");
        _sourceCombatActs = binder.BindInstanceValue<string[]>(
            typeof(SourceChara.Row),
            GameValueAccess.Read,
            "actCombat");
        _useAbility = binder.BindInstanceMethod(
            typeof(Chara),
            typeof(bool),
            new[] { typeof(Act), typeof(Card), typeof(Point), typeof(bool) },
            "UseAbility");
        _layerList = binder.BindInstanceValue<UIDynamicList>(
            typeof(LayerAbility),
            GameValueAccess.Read,
            "list");
        _layerCharacter = binder.BindInstanceValue<Chara>(
            typeof(LayerAbility),
            GameValueAccess.Read,
            "chara");
        _layerGroups = binder.BindInstanceValue<string[]>(
            typeof(LayerAbility),
            GameValueAccess.Read,
            "idGroup");
        _favoriteAbilities = binder.BindInstanceValue<HashSet<int>>(
            typeof(Player),
            GameValueAccess.Read,
            "favAbility");
        _buttonAct = binder.BindInstanceValue<Act>(
            typeof(ButtonAbility),
            GameValueAccess.ReadWrite,
            "act");
        _buttonStockText = binder.BindInstanceValue<UIText>(
            typeof(ButtonAbility),
            GameValueAccess.Read,
            "textStock");
        _addToList = binder.BindInstanceMethod(
            typeof(BaseList),
            typeof(void),
            new[] { typeof(object) },
            "Add");
        _selectGroup = binder.BindInstanceMethod(
            typeof(LayerAbility),
            typeof(void),
            new[] { typeof(string) },
            "SelectGroup");

        var callbackType =
            AllowPcGeneImplantReflection.AbilityListCallback.Value?.DeclaringType;
        if (callbackType != null)
        {
            _callbackLayer = binder.BindInstanceValue<LayerAbility>(
                callbackType,
                GameValueAccess.Read,
                "<>4__this");
            _callbackGroup = binder.BindInstanceValue<string>(
                callbackType,
                GameValueAccess.Read,
                "id");
        }
    }

    internal void Reset(bool redraw)
    {
        Synchronize(false, redraw);
    }

    internal void Synchronize(bool enabled, bool redraw)
    {
        var changed = false;
        try
        {
            var character = _characters.PlayerCharacter;
            var elements = character == null
                ? null
                : _characters.GetElements(character);
            var sourceTable = _sources.Elements;
            if (character == null || elements == null || sourceTable == null ||
                !_sourceRows.TryGet(sourceTable, out var rows) || rows == null)
            {
                changed = Clear();
                CompleteSynchronization(changed, redraw);
                return;
            }

            var candidates = CollectCandidates(character, rows);
            changed = CleanLegacyState(character, elements, candidates);
            if (!enabled)
            {
                changed = Clear() || changed;
                CompleteSynchronization(changed, redraw);
                return;
            }

            if (!ReferenceEquals(character, _projectedCharacter) ||
                !ReferenceEquals(elements, _projectedElements))
            {
                changed = Clear();
                _projectedCharacter = character;
                _projectedElements = elements;
                changed = true;
            }

            var staleIds = new List<int>();
            foreach (var entry in _abilities)
            {
                if (!candidates.ContainsKey(entry.Key))
                    staleIds.Add(entry.Key);
            }
            foreach (var id in staleIds)
            {
                _abilities.Remove(id);
                changed = true;
            }

            foreach (var entry in candidates)
            {
                var id = entry.Key;
                var candidate = entry.Value;
                if (!_abilities.TryGetValue(id, out var projected) ||
                    !ReferenceEquals(projected.Source, candidate.Source))
                {
                    projected = CreateProjection(
                        id,
                        candidate.Source,
                        candidate.Count,
                        elements);
                    if (projected == null)
                    {
                        if (_abilities.Remove(id))
                            changed = true;
                        continue;
                    }
                    _abilities[id] = projected;
                    changed = true;
                }
                else if (projected.GeneCount != candidate.Count)
                {
                    projected.GeneCount = candidate.Count;
                    changed = true;
                }

                var nativeElementActive = HasActiveNativeElement(elements, id);
                if (projected.NativeElementActive != nativeElementActive)
                {
                    projected.NativeElementActive = nativeElementActive;
                    changed = true;
                }
            }
        }
        catch
        {
            changed = Clear();
        }

        CompleteSynchronization(changed, redraw);
    }

    internal void AppendToAbilityList(
        bool enabled,
        object callbackOwner,
        UIList.SortMode sortMode)
    {
        if (!enabled || callbackOwner == null ||
            _callbackLayer == null || _callbackGroup == null ||
            !_callbackLayer.TryGet(callbackOwner, out var layer) || layer == null ||
            !_callbackGroup.TryGet(callbackOwner, out var group) ||
            string.IsNullOrEmpty(group))
            return;

        _selectedLayer = layer;
        _selectedGroup = group;
        Synchronize(true, false);
        if (_abilities.Count == 0 ||
            !_layerCharacter.TryGet(layer, out var character) ||
            !ReferenceEquals(character, _characters.PlayerCharacter) ||
            !_layerList.TryGet(layer, out var list) || list == null)
            return;

        _layerGroups.TryGet(layer, out var groups);
        var visible = new List<ProjectedAbility>();
        foreach (var projected in _abilities.Values)
        {
            if (!projected.NativeElementActive &&
                MatchesGroup(projected, group, groups))
                visible.Add(projected);
        }
        visible.Sort((left, right) =>
            GetSortValue(left.Act, sortMode).CompareTo(
                GetSortValue(right.Act, sortMode)));
        foreach (var projected in visible)
            _addToList.TryInvoke(
                list,
                new object?[] { projected.Act },
                out _);
    }

    internal void ApplyButtonAct(
        bool enabled,
        ButtonAbility button,
        Chara character,
        Element element)
    {
        if (!enabled || button == null || character == null || element == null ||
            !ReferenceEquals(character, _characters.PlayerCharacter))
            return;
        Synchronize(true, false);
        if (!_elementId.TryGet(element, out var elementId))
            return;
        foreach (var projected in _abilities.Values)
        {
            if (projected.Id != elementId)
                continue;
            if (!projected.NativeElementActive &&
                ReferenceEquals(projected.Act, element))
                _buttonAct.TrySet(button, projected.Act);
            if (_buttonStockText.TryGet(button, out var stockText) &&
                stockText != null)
            {
                stockText.SetActive(true);
                stockText.text = "∞";
            }
            return;
        }
    }

    internal bool TryUse(
        bool enabled,
        Chara character,
        int id,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        result = false;
        if (!enabled || character == null || id <= 0 ||
            !ReferenceEquals(character, _characters.PlayerCharacter))
            return false;

        Synchronize(true, false);
        if (!_abilities.TryGetValue(id, out var projected) ||
            projected.NativeElementActive ||
            HasNativeAbilitySource(character, projected))
            return false;

        if (!_useAbility.TryInvoke(
                character,
                new object?[] { projected.Act, target, point, partyTarget },
                out var invocationResult) ||
            invocationResult is not bool used)
            return false;

        result = used;
        return true;
    }

    internal bool TryUse(
        bool enabled,
        Chara character,
        string alias,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        result = false;
        if (!enabled || string.IsNullOrEmpty(alias))
            return false;
        Synchronize(true, false);
        foreach (var projected in _abilities.Values)
        {
            if (string.Equals(projected.Alias, alias, StringComparison.Ordinal))
                return TryUse(
                    true,
                    character,
                    projected.Id,
                    target,
                    point,
                    partyTarget,
                    out result);
        }
        return false;
    }

    internal bool IsProjectedAbility(
        bool enabled,
        CharaAbility ability,
        int id)
    {
        if (!enabled || ability == null || id <= 0)
            return false;
        var character = _characters.PlayerCharacter;
        if (character == null ||
            !_characterAbility.TryGet(character, out var playerAbility) ||
            !ReferenceEquals(ability, playerAbility))
            return false;
        Synchronize(true, false);
        return _abilities.ContainsKey(id);
    }

    internal void RefreshFor(bool enabled, Chara character)
    {
        if (character != null &&
            ReferenceEquals(character, _characters.PlayerCharacter))
            Synchronize(enabled, true);
    }

    private Dictionary<int, Candidate> CollectCandidates(
        Chara character,
        Dictionary<int, SourceElement.Row> rows)
    {
        var result = new Dictionary<int, Candidate>();
        if (!_characterGenes.TryGet(character, out var genes) || genes == null ||
            !_installedGenes.TryGet(genes, out var installed) || installed == null)
            return result;

        foreach (var gene in installed)
        {
            if (gene == null ||
                !_geneType.TryGet(gene, out var type) ||
                type == DNA.Type.Brain || type == DNA.Type.Inferior ||
                !_geneValues.TryGet(gene, out var values) || values == null)
                continue;

            for (var index = 0; index + 1 < values.Count; index += 2)
            {
                var id = values[index];
                var negative = values[index + 1] < 0;
                if (id <= 0 || !rows.TryGetValue(id, out var source) ||
                    source == null ||
                    !_sourceCategory.TryGet(source, out var category) ||
                    !string.Equals(category, "ability", StringComparison.Ordinal))
                    continue;

                if (result.TryGetValue(id, out var candidate))
                    candidate.Increment(negative);
                else
                    result.Add(id, new Candidate(source, negative));
            }
        }

        return result;
    }

    private bool CleanLegacyState(
        Chara character,
        ElementContainer elements,
        Dictionary<int, Candidate> candidates)
    {
        if (!ReferenceEquals(character, _legacyCleanupCharacter))
        {
            _legacyCleanupCharacter = character;
            _legacyCleanedIds.Clear();
        }
        if (!_storedAbilities.IsBound)
            return false;

        _storedAbilities.TryGet(character, out var stored);
        var changed = false;
        var listChanged = false;
        foreach (var entry in candidates)
        {
            if (!_legacyCleanedIds.Add(entry.Key))
                continue;

            var removed = RemoveStoredEntries(stored, entry.Key, entry.Value);
            if (removed == 0)
                continue;

            changed = true;
            listChanged = true;
            if (HasStoredEntry(stored, entry.Key) ||
                HasTemplateAbility(character, entry.Value.Source) ||
                !_getElement.TryInvoke(
                    elements,
                    new object?[] { entry.Key },
                    out var value) ||
                value is not Element element ||
                !IsLegacyGeneElement(element))
                continue;

            _modBase.TryInvoke(
                elements,
                new object?[] { entry.Key, -1 },
                out _);
        }

        if (stored != null && stored.Count == 0)
            _storedAbilities.TrySet(character, null!);
        if (listChanged &&
            _characterAbility.TryGet(character, out var ability) && ability != null)
            _refreshAbility.TryInvoke(ability, Array.Empty<object?>(), out _);
        return changed;
    }

    private static int RemoveStoredEntries(
        List<int>? stored,
        int id,
        Candidate candidate)
    {
        if (stored == null)
            return 0;
        return RemoveStoredEntries(stored, id, candidate.PositiveCount) +
               RemoveStoredEntries(stored, -id, candidate.NegativeCount);
    }

    private static int RemoveStoredEntries(
        List<int> stored,
        int id,
        int count)
    {
        var removed = 0;
        for (var index = stored.Count - 1; index >= 0 && removed < count; index--)
        {
            if (stored[index] != id)
                continue;
            stored.RemoveAt(index);
            removed++;
        }
        return removed;
    }

    private static bool HasStoredEntry(List<int>? stored, int id)
    {
        if (stored == null)
            return false;
        foreach (var entry in stored)
            if (Math.Abs(entry) == id)
                return true;
        return false;
    }

    private bool HasTemplateAbility(Chara character, SourceElement.Row source)
    {
        if (!_sourceAlias.TryGet(source, out var alias) ||
            string.IsNullOrEmpty(alias) ||
            !_characterSource.TryGet(character, out var characterSource) ||
            characterSource == null ||
            !_sourceCombatActs.TryGet(characterSource, out var acts) || acts == null)
            return false;
        foreach (var entry in acts)
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            var separator = entry.IndexOf('/');
            var entryAlias = separator < 0 ? entry : entry.Substring(0, separator);
            if (string.Equals(entryAlias, alias, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private bool IsLegacyGeneElement(Element element)
    {
        return _elementBase.TryGet(element, out var baseValue) && baseValue == 1 &&
               _elementSourceValue.TryGet(element, out var sourceValue) &&
               sourceValue == 0 &&
               _elementLink.TryGet(element, out var link) && link == 0;
    }

    private ProjectedAbility? CreateProjection(
        int id,
        SourceElement.Row source,
        int geneCount,
        ElementContainer elements)
    {
        if (!_sourceAlias.TryGet(source, out var alias) ||
            string.IsNullOrEmpty(alias) ||
            !_sourceCategorySub.TryGet(source, out var categorySub) ||
            !_createElement.TryInvoke(
                null,
                new object?[] { id, 1 },
                out var created) ||
            created is not Act act ||
            !_elementOwner.TrySet(act, elements))
            return null;

        return new ProjectedAbility(
            id,
            alias,
            categorySub ?? string.Empty,
            source,
            act,
            geneCount);
    }

    private bool HasActiveNativeElement(ElementContainer elements, int id)
    {
        return _getElement.TryInvoke(
                   elements,
                   new object?[] { id },
                   out var value) &&
               value is Element element &&
               _elementValue.TryGet(element, out var elementValue) &&
               elementValue != 0;
    }

    private bool HasNativeAbilitySource(
        Chara character,
        ProjectedAbility projected)
    {
        var elements = _characters.GetElements(character);
        if (elements != null && HasActiveNativeElement(elements, projected.Id))
            return true;

        if (!_characterAbility.TryGet(character, out var ability) || ability == null ||
            !_abilityList.TryGet(ability, out var actList) || actList == null ||
            !_abilityItems.TryGet(actList, out var items) || items == null)
            return true;

        var count = 0;
        foreach (var item in items)
        {
            if (item != null &&
                _abilityItemAct.TryGet(item, out var act) && act != null &&
                _elementId.TryGet(act, out var id) && id == projected.Id)
                count++;
        }
        return count > projected.GeneCount;
    }

    private bool MatchesGroup(
        ProjectedAbility projected,
        string group,
        string[]? groups)
    {
        if (string.Equals(group, "favAbility", StringComparison.Ordinal))
        {
            var player = _runtime.Player;
            return player != null &&
                   _favoriteAbilities.TryGet(player, out var favorites) &&
                   favorites != null && favorites.Contains(projected.Id);
        }
        if (string.Equals(
                group,
                projected.CategorySub,
                StringComparison.Ordinal))
            return true;
        if (!string.Equals(group, "all", StringComparison.Ordinal))
            return false;
        if (groups == null)
            return true;
        for (var index = 0; index < groups.Length; index++)
        {
            if (string.Equals(
                    groups[index],
                    projected.CategorySub,
                    StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private int GetSortValue(Element element, UIList.SortMode sortMode)
    {
        return _getSortValue.TryInvoke(
                   element,
                   new object?[] { sortMode },
                   out var value) && value is int result
            ? result
            : 0;
    }

    private bool Clear()
    {
        var changed = _abilities.Count > 0 ||
                      _projectedCharacter != null ||
                      _projectedElements != null;
        _abilities.Clear();
        _projectedCharacter = null;
        _projectedElements = null;
        return changed;
    }

    private void CompleteSynchronization(bool changed, bool redraw)
    {
        if (changed && !redraw)
        {
            _redrawPending = true;
            return;
        }
        if (!redraw || (!changed && !_redrawPending))
            return;
        _redrawPending = false;
        if (_selectedLayer != null &&
            !string.IsNullOrEmpty(_selectedGroup) &&
            _selectGroup.TryInvoke(
                _selectedLayer,
                new object?[] { _selectedGroup },
                out _))
            return;
        try
        {
            LayerAbility.Redraw();
        }
        catch
        {
        }
    }
}

internal sealed partial class AllowPcGeneImplantModule
{
    internal void AppendProjectedAbilities(
        object callbackOwner,
        UIList.SortMode sortMode)
    {
        _abilityProjection.AppendToAbilityList(
            Enabled,
            callbackOwner,
            sortMode);
    }

    internal void ApplyProjectedButtonAct(
        ButtonAbility button,
        Chara character,
        Element element)
    {
        _abilityProjection.ApplyButtonAct(
            Enabled,
            button,
            character,
            element);
    }

    internal bool TryUseProjectedAbility(
        Chara character,
        int id,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        return _abilityProjection.TryUse(
            Enabled,
            character,
            id,
            target,
            point,
            partyTarget,
            out result);
    }

    internal bool TryUseProjectedAbility(
        Chara character,
        string alias,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        return _abilityProjection.TryUse(
            Enabled,
            character,
            alias,
            target,
            point,
            partyTarget,
            out result);
    }

    internal bool IsProjectedAbility(CharaAbility ability, int id)
    {
        return _abilityProjection.IsProjectedAbility(
            Enabled,
            ability,
            id);
    }

    internal void RefreshAbilityProjection(Chara character)
    {
        _abilityProjection.RefreshFor(Enabled, character);
    }
}

internal static partial class AllowPcGeneImplantPatchContext
{
    internal static void AppendProjectedAbilities(
        object callbackOwner,
        UIList.SortMode sortMode)
    {
        Current?.AppendProjectedAbilities(callbackOwner, sortMode);
    }

    internal static void ApplyProjectedButtonAct(
        ButtonAbility button,
        Chara character,
        Element element)
    {
        Current?.ApplyProjectedButtonAct(button, character, element);
    }

    internal static bool TryUseProjectedAbility(
        Chara character,
        int id,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        result = false;
        return Current?.TryUseProjectedAbility(
                   character,
                   id,
                   target,
                   point,
                   partyTarget,
                   out result) == true;
    }

    internal static bool TryUseProjectedAbility(
        Chara character,
        string alias,
        Card? target,
        Point? point,
        bool partyTarget,
        out bool result)
    {
        result = false;
        return Current?.TryUseProjectedAbility(
                   character,
                   alias,
                   target,
                   point,
                   partyTarget,
                   out result) == true;
    }

    internal static bool IsProjectedAbility(CharaAbility ability, int id)
    {
        return Current?.IsProjectedAbility(ability, id) == true;
    }

    internal static void RefreshAbilityProjection(Chara character)
    {
        Current?.RefreshAbilityProjection(character);
    }
}

internal static partial class AllowPcGeneImplantReflection
{
    internal static readonly Lazy<MethodInfo?> AbilityListCallback =
        new Lazy<MethodInfo?>(ResolveAbilityListCallback);

    private static MethodInfo? ResolveAbilityListCallback()
    {
        var flags = BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic;
        var candidates = new List<MethodInfo>();
        Type[] nestedTypes;
        try
        {
            nestedTypes = typeof(LayerAbility).GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }

        foreach (var type in nestedTypes)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(flags);
            }
            catch
            {
                continue;
            }
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (!method.IsStatic && method.ReturnType == typeof(void) &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(UIList.SortMode))
                    candidates.Add(method);
            }
        }

        var dictionaryField = AccessTools.Field(
            typeof(ElementContainer),
            "dict");
        var addMethod = AccessTools.Method(
            typeof(BaseList),
            "Add",
            new[] { typeof(object) });
        if (dictionaryField != null && addMethod != null)
        {
            foreach (var method in candidates)
            {
                if (method.Name.IndexOf(
                        "<SelectGroup>b__",
                        StringComparison.Ordinal) >= 0 &&
                    ReadsOperand(method, dictionaryField) &&
                    ReadsOperand(method, addMethod))
                    return method;
            }
            foreach (var method in candidates)
            {
                if (ReadsOperand(method, dictionaryField) &&
                    ReadsOperand(method, addMethod))
                    return method;
            }
        }

        return candidates.Find(method =>
            method.Name.IndexOf(
                "<SelectGroup>b__",
                StringComparison.Ordinal) >= 0);
    }
}

[HarmonyPatch]
internal static class LayerAbilityListPcGeneAbilityProjectionPatch
{
    private static MethodBase? TargetMethod()
    {
        return AllowPcGeneImplantReflection.AbilityListCallback.Value;
    }

    private static void Postfix(object __instance, UIList.SortMode __0)
    {
        AllowPcGeneImplantPatchContext.AppendProjectedAbilities(
            __instance,
            __0);
    }
}

[HarmonyPatch(typeof(ButtonAbility), "SetAct", new[] { typeof(Chara), typeof(Element) })]
internal static class ButtonAbilityPcGeneAbilityProjectionPatch
{
    private static void Postfix(
        ButtonAbility __instance,
        Chara __0,
        Element __1)
    {
        AllowPcGeneImplantPatchContext.ApplyProjectedButtonAct(
            __instance,
            __0,
            __1);
    }
}

[HarmonyPatch(typeof(Chara), "UseAbility", new[]
{
    typeof(int), typeof(Card), typeof(Point), typeof(bool)
})]
internal static class CharaUseAbilityByIdPcGeneAbilityProjectionPatch
{
    private static bool Prefix(
        Chara __instance,
        int __0,
        Card? __1,
        Point? __2,
        bool __3,
        ref bool __result)
    {
        if (!AllowPcGeneImplantPatchContext.TryUseProjectedAbility(
                __instance,
                __0,
                __1,
                __2,
                __3,
                out var result))
            return true;
        __result = result;
        return false;
    }
}

[HarmonyPatch(typeof(Chara), "UseAbility", new[]
{
    typeof(string), typeof(Card), typeof(Point), typeof(bool)
})]
internal static class CharaUseAbilityByAliasPcGeneAbilityProjectionPatch
{
    private static bool Prefix(
        Chara __instance,
        string __0,
        Card? __1,
        Point? __2,
        bool __3,
        ref bool __result)
    {
        if (!AllowPcGeneImplantPatchContext.TryUseProjectedAbility(
                __instance,
                __0,
                __1,
                __2,
                __3,
                out var result))
            return true;
        __result = result;
        return false;
    }
}

[HarmonyPatch(typeof(CharaAbility), "Has", new[] { typeof(int) })]
internal static class CharaAbilityHasPcGeneAbilityProjectionPatch
{
    private static void Postfix(
        CharaAbility __instance,
        int __0,
        ref bool __result)
    {
        if (!__result &&
            AllowPcGeneImplantPatchContext.IsProjectedAbility(
                __instance,
                __0))
            __result = true;
    }
}

[HarmonyPatch(typeof(DNA), "Apply", new[] { typeof(Chara) })]
internal static class DnaApplyPcGeneAbilityProjectionPatch
{
    private static void Postfix(Chara __0)
    {
        AllowPcGeneImplantPatchContext.RefreshAbilityProjection(__0);
    }
}

[HarmonyPatch(typeof(DNA), "Apply", new[] { typeof(Chara), typeof(bool) })]
internal static class DnaReverseApplyPcGeneAbilityProjectionPatch
{
    private static void Postfix(Chara __0, bool __1)
    {
        if (__1)
            AllowPcGeneImplantPatchContext.RefreshAbilityProjection(__0);
    }
}

[HarmonyPatch(typeof(CharaGenes), "Remove", new[] { typeof(Chara), typeof(DNA) })]
internal static class CharaGenesRemovePcGeneAbilityProjectionPatch
{
    private static void Postfix(Chara __0)
    {
        AllowPcGeneImplantPatchContext.RefreshAbilityProjection(__0);
    }
}
