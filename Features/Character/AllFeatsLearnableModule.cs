using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

internal sealed class AllFeatsLearnableModule
{
    internal const string ListMarker = "__ElinModifierAllFeats__";

    private readonly ElinModifierPlugin _host;
    private readonly IGameSourceRepository _sources;
    private readonly ICharacterGameAccess _characters;
    private readonly IBoundGameMethod _createElement;
    private readonly IBoundGameMethod _getElement;
    private readonly IBoundGameValue<int> _elementValueWithoutLink;
    private readonly IBoundGameValue<List<SourceElement.Row>> _sourceRows;
    private readonly IBoundGameValue<int> _rowId;
    private readonly IBoundGameValue<string> _rowGroup;
    private readonly IBoundGameValue<string> _rowCategorySub;
    private readonly IBoundGameValue<int[]> _rowCost;
    private readonly IBoundGameValue<int> _rowMax;
    private readonly IBoundGameValue<string[]> _rowTags;
    private readonly FieldInfo? _subCategoryField;
    private readonly FieldInfo? _listField;
    private readonly FieldInfo? _parentField;
    private readonly FieldInfo? _windowField;
    private readonly FieldInfo? _characterField;

    internal AllFeatsLearnableModule(
        ElinModifierPlugin host,
        IGameSourceRepository sources,
        ICharacterGameAccess characters,
        IGameMemberBinder binder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _createElement = binder.BindStaticMethod(
            typeof(Element),
            typeof(Element),
            new[] { typeof(int), typeof(int) },
            "Create");
        _getElement = binder.BindInstanceMethod(
            typeof(ElementContainer),
            typeof(Element),
            new[] { typeof(int) },
            "GetElement");
        _elementValueWithoutLink = binder.BindInstanceValue<int>(
            typeof(Element),
            GameValueAccess.Read,
            "ValueWithoutLink");
        _sourceRows = binder.BindInstanceValue<List<SourceElement.Row>>(
            typeof(SourceElement),
            GameValueAccess.Read,
            "rows");
        _rowId = binder.BindInstanceValue<int>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "id");
        _rowGroup = binder.BindInstanceValue<string>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "group");
        _rowCategorySub = binder.BindInstanceValue<string>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "categorySub");
        _rowCost = binder.BindInstanceValue<int[]>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "cost");
        _rowMax = binder.BindInstanceValue<int>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "max");
        _rowTags = binder.BindInstanceValue<string[]>(
            typeof(SourceElement.Row),
            GameValueAccess.Read,
            "tag");

        var callback = AllFeatsLearnableReflection.PurchaseListCallback.Value;
        var listContextType = callback?.DeclaringType;
        var mainContextType = AllFeatsLearnableReflection.PurchaseListBuilder.Value?.DeclaringType;
        if (listContextType != null && mainContextType != null)
        {
            _subCategoryField = FindInstanceField(listContextType, typeof(string), "idSubCat");
            _listField = FindInstanceField(listContextType, typeof(UIList), "_list");
            _parentField = FindInstanceField(listContextType, mainContextType, "CS$<>8__locals2");
            _windowField = FindInstanceField(mainContextType, typeof(WindowChara), "<>4__this");
        }

        _characterField = AccessTools.Field(typeof(WindowChara), "chara");
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
    }

    internal void Reset()
    {
        Enabled = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }

    internal string GetHeaderText()
    {
        return _host.TranslateModuleText(
            "可选专长(Elin Modifier)",
            "Available feats (Elin Modifier)");
    }

    internal bool TryPopulateAllFeats(object context)
    {
        if (!Enabled || context == null ||
            _subCategoryField == null || _listField == null ||
            _parentField == null || _windowField == null || _characterField == null)
            return false;

        try
        {
            if (!string.Equals(
                    _subCategoryField.GetValue(context) as string,
                    ListMarker,
                    StringComparison.Ordinal))
                return false;

            var list = _listField.GetValue(context) as UIList;
            var parent = _parentField.GetValue(context);
            var window = parent == null ? null : _windowField.GetValue(parent) as WindowChara;
            var character = window == null ? null : _characterField.GetValue(window) as Chara;
            if (list == null || character == null)
                return true;

            var source = _sources.Elements;
            if (source == null ||
                !_sourceRows.TryGet(source, out var rows) ||
                rows == null)
                return true;

            var seen = new HashSet<int>();
            foreach (var row in rows)
            {
                if (row == null ||
                    !_rowGroup.TryGet(row, out var group) ||
                    !string.Equals(group, "FEAT", StringComparison.OrdinalIgnoreCase) ||
                    !_rowId.TryGet(row, out var id) ||
                    id <= 0 ||
                    !seen.Add(id))
                    continue;

                if (!TryGetCurrentFeatLevel(character, id, out var currentLevel))
                    currentLevel = 0;
                var nextLevel = Math.Max(0, currentLevel) + 1;
                _rowMax.TryGet(row, out var maxLevel);
                if ((maxLevel > 0 && nextLevel > maxLevel) ||
                    (maxLevel <= 0 && currentLevel > 0))
                    continue;

                if (!_createElement.TryInvoke(
                        null,
                        new object?[] { id, nextLevel },
                        out var created) ||
                    created is not Feat feat)
                    continue;

                if (RequiresFivePointFallback(row, nextLevel))
                    AllFeatsLearnableCostOverrides.Mark(feat);
                list.Add(feat);
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    private bool TryGetCurrentFeatLevel(Chara character, int id, out int level)
    {
        level = 0;
        var elements = _characters.GetElements(character);
        if (elements == null)
            return false;
        if (!_getElement.TryInvoke(
                elements,
                new object?[] { id },
                out var value))
            return false;
        if (value is not Element element)
            return true;
        return _elementValueWithoutLink.TryGet(element, out level);
    }

    private bool RequiresFivePointFallback(SourceElement.Row row, int nextLevel)
    {
        _rowCategorySub.TryGet(row, out var categorySub);
        _rowCost.TryGet(row, out var costs);
        _rowTags.TryGet(row, out var tags);
        if (string.IsNullOrEmpty(categorySub) ||
            costs == null ||
            costs.Length == 0 ||
            nextLevel > costs.Length ||
            costs[nextLevel - 1] < 0)
            return true;
        if (tags == null)
            return false;
        foreach (var tag in tags)
            if (string.Equals(tag, "class", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "hidden", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "innate", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static FieldInfo? FindInstanceField(
        Type declaringType,
        Type fieldType,
        string preferredName)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var preferred = declaringType.GetField(preferredName, flags);
        if (preferred != null && preferred.FieldType == fieldType)
            return preferred;
        return declaringType.GetFields(flags).FirstOrDefault(field => field.FieldType == fieldType);
    }
}

internal static class AllFeatsLearnableCostOverrides
{
    private sealed class Marker
    {
    }

    private static readonly ConditionalWeakTable<Feat, Marker> Marked =
        new ConditionalWeakTable<Feat, Marker>();

    internal static void Mark(Feat feat)
    {
        Marked.Remove(feat);
        Marked.Add(feat, new Marker());
    }

    internal static void Apply(Feat feat, ref int result)
    {
        if (feat != null && Marked.TryGetValue(feat, out _))
            result = 5;
    }
}

internal static class AllFeatsLearnablePatchContext
{
    internal static AllFeatsLearnableModule? Current =>
        ElinModifierPlugin.ActiveModules?.AllFeatsLearnable;

    internal static string GetHeaderText()
    {
        return Current?.GetHeaderText() ?? "可选专长(Elin Modifier)";
    }

    internal static void AppendAllFeatsSection(object context)
    {
        if (Current?.Enabled != true || context == null)
            return;
        try
        {
            AllFeatsLearnableReflection.PurchaseListBuilder.Value?.Invoke(
                context,
                new object?[] { GetHeaderText(), AllFeatsLearnableModule.ListMarker });
        }
        catch
        {
        }
    }

    internal static bool ShouldAllowPurchase(bool original)
    {
        return original || Current?.Enabled == true;
    }
}

internal static class AllFeatsLearnableReflection
{
    internal static readonly Lazy<MethodInfo?> PurchaseListBuilder =
        new Lazy<MethodInfo?>(ResolvePurchaseListBuilder);

    internal static readonly Lazy<MethodInfo?> PurchaseListCallback =
        new Lazy<MethodInfo?>(ResolvePurchaseListCallback);

    internal static readonly Lazy<MethodInfo?> PurchaseClickCallback =
        new Lazy<MethodInfo?>(ResolvePurchaseClickCallback);

    private static MethodInfo? ResolvePurchaseListBuilder()
    {
        var flags = BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var candidates = typeof(WindowChara)
                .GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(flags))
                .Where(
                    method =>
                    {
                        var parameters = method.GetParameters();
                        return method.ReturnType == typeof(void) &&
                               parameters.Length == 2 &&
                               parameters[0].ParameterType == typeof(string) &&
                               parameters[1].ParameterType == typeof(string) &&
                               method.Name.StartsWith(
                                   "<RefreshSkill>g__List|",
                                   StringComparison.Ordinal);
                    })
                .ToList();
            return candidates.FirstOrDefault(
                       method => string.Equals(
                           method.Name,
                           "<RefreshSkill>g__List|6",
                           StringComparison.Ordinal)) ??
                   candidates.OrderBy(method => method.Name, StringComparer.Ordinal).LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static MethodInfo? ResolvePurchaseListCallback()
    {
        var listMethod = AccessTools.Method(
            typeof(Chara),
            "ListAvailabeFeats",
            new[] { typeof(bool), typeof(bool) }) ??
            AccessTools.Method(
                typeof(Chara),
                "ListAvailableFeats",
                new[] { typeof(bool), typeof(bool) });
        if (listMethod == null)
            return null;

        return FindNestedMethod(
            method =>
            {
                var parameters = method.GetParameters();
                return method.ReturnType == typeof(void) &&
                       parameters.Length == 1 &&
                       parameters[0].ParameterType == typeof(UIList.SortMode) &&
                       ReadsOperand(method, listMethod);
            });
    }

    private static MethodInfo? ResolvePurchaseClickCallback()
    {
        var requirementMethod = AccessTools.Method(
            typeof(Element),
            "IsPurchaseFeatReqMet",
            new[] { typeof(ElementContainer), typeof(int) });
        if (requirementMethod == null)
            return null;

        return FindNestedMethod(
            method =>
            {
                var parameters = method.GetParameters();
                return method.ReturnType == typeof(void) &&
                       parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Element) &&
                       parameters[1].ParameterType == typeof(ButtonElement) &&
                       ReadsOperand(method, requirementMethod);
            });
    }

    private static MethodInfo? FindNestedMethod(Func<MethodInfo, bool> predicate)
    {
        var flags = BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            foreach (var nestedType in typeof(WindowChara).GetNestedTypes(BindingFlags.NonPublic))
                foreach (var method in nestedType.GetMethods(flags))
                    if (predicate(method))
                        return method;
        }
        catch
        {
        }
        return null;
    }

    private static bool ReadsOperand(MethodInfo method, MemberInfo target)
    {
        try
        {
            foreach (var entry in PatchProcessor.ReadMethodBody(method))
                if (entry.Value is MemberInfo member && IsSameMember(member, target))
                    return true;
        }
        catch
        {
        }
        return false;
    }

    private static bool IsSameMember(MemberInfo left, MemberInfo right)
    {
        if (ReferenceEquals(left, right) || Equals(left, right))
            return true;
        try
        {
            if (left.Module == right.Module && left.MetadataToken == right.MetadataToken)
                return true;
        }
        catch
        {
        }
        return left.MemberType == right.MemberType &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(
                   left.DeclaringType?.FullName,
                   right.DeclaringType?.FullName,
                   StringComparison.Ordinal);
    }
}

[HarmonyPatch]
internal static class WindowCharaPurchaseListBuilderAllFeatsLearnablePatch
{
    private static MethodBase? TargetMethod()
    {
        return AllFeatsLearnableReflection.PurchaseListBuilder.Value;
    }

    private static void Postfix(object __instance, string idSubCat)
    {
        if (string.Equals(idSubCat, "attribute", StringComparison.Ordinal))
            AllFeatsLearnablePatchContext.AppendAllFeatsSection(__instance);
    }
}

[HarmonyPatch]
internal static class WindowCharaPurchaseListAllFeatsLearnablePatch
{
    private static MethodBase? TargetMethod()
    {
        return AllFeatsLearnableReflection.PurchaseListCallback.Value;
    }

    private static bool Prefix(object __instance)
    {
        return AllFeatsLearnablePatchContext.Current?.TryPopulateAllFeats(__instance) != true;
    }
}

[HarmonyPatch]
internal static class WindowCharaPurchaseClickAllFeatsLearnablePatch
{
    private static MethodBase? TargetMethod()
    {
        return AllFeatsLearnableReflection.PurchaseClickCallback.Value;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var requirementMethod = AccessTools.Method(
            typeof(Element),
            "IsPurchaseFeatReqMet",
            new[] { typeof(ElementContainer), typeof(int) });
        var helper = AccessTools.Method(
            typeof(AllFeatsLearnablePatchContext),
            "ShouldAllowPurchase");
        if (requirementMethod == null || helper == null)
            return codes;

        for (var i = 0; i < codes.Count; i++)
        {
            if (!codes[i].Calls(requirementMethod))
                continue;
            codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
            break;
        }
        return codes;
    }
}

[HarmonyPatch]
internal static class FeatCostLearnAllFeatsLearnablePatch
{
    private static MethodBase? TargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(Feat), "CostLearn");
    }

    private static void Postfix(Feat __instance, ref int __result)
    {
        AllFeatsLearnableCostOverrides.Apply(__instance, ref __result);
    }
}
