using System;
using System.Collections.Generic;
using HarmonyLib;

public sealed partial class ElinModifierPlugin
{
    [ThreadStatic]
    private static bool _shrineEffectSelectionExecuting;

    [ThreadStatic]
    private static ShrineEffectOutcome? _selectedShrineEffectOutcome;

    private sealed class ShrineEffectOutcome
    {
        internal ShrineEffectOutcome(
            string label,
            SourceMaterial.Row? material = null,
            string? itemId = null,
            string? recipeId = null)
        {
            Label = label;
            Material = material;
            ItemId = itemId;
            RecipeId = recipeId;
        }

        internal string Label { get; }
        internal SourceMaterial.Row? Material { get; }
        internal string? ItemId { get; }
        internal string? RecipeId { get; }
    }

    private sealed class ShrinePagedDialogEntry<TValue> where TValue : class
    {
        internal ShrinePagedDialogEntry(string label, TValue? value = null, int targetPage = -1)
        {
            Label = label;
            Value = value;
            TargetPage = targetPage;
        }

        internal string Label { get; }
        internal TValue? Value { get; }
        internal int TargetPage { get; }
    }

    private void SetShrineEffectSelection(bool enabled)
    {
        if (_shrineEffectSelection == enabled)
            return;
        _shrineEffectSelection = enabled;
        _log = enabled
            ? T("神龛自选效果已开启", "Shrine effect selection enabled")
            : T("神龛自选效果已关闭", "Shrine effect selection disabled");
    }

    private bool TryOpenShrineEffectSelection(TraitShrine shrine, Chara user)
    {
        try
        {
            var registered = GameAccess.Runtime.GameData?.shrines;
            if (registered == null || registered.Count == 0 || shrine.owner == null || !shrine.owner.isOn)
                return false;

            var choices = new List<ShrineData>(registered);
            OpenPagedShrineList(
                T("神龛自选效果", "Select shrine effect"),
                choices,
                GetShrineEffectSelectionLabel,
                selected => OpenShrineOutcomeSelectionOrExecute(shrine, user, selected));
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Shrine effect selection failed to open: " + ex.Message);
            return false;
        }
    }

    private static string GetShrineEffectSelectionLabel(ShrineData shrine)
    {
        if (shrine == null || string.IsNullOrEmpty(shrine.id))
            return "?";

        var key = "shrine_" + shrine.id;
        string label;
        try { label = key.lang(""); }
        catch { label = key; }
        if (string.IsNullOrWhiteSpace(label) || string.Equals(label, key, StringComparison.Ordinal))
            label = shrine.id;
        return label + " [" + shrine.id + "]";
    }

    private void OpenShrineOutcomeSelectionOrExecute(
        TraitShrine shrine,
        Chara user,
        ShrineData selected)
    {
        if (selected == null)
            return;

        var outcomes = BuildShrineEffectOutcomes(shrine, selected);
        if (outcomes.Count <= 1)
        {
            ExecuteSelectedShrineEffect(
                shrine,
                user,
                selected,
                outcomes.Count == 1 ? outcomes[0] : null);
            return;
        }

        OpenPagedShrineList(
            T("选择效果", "Select effect"),
            outcomes,
            outcome => outcome.Label,
            outcome => ExecuteSelectedShrineEffect(shrine, user, selected, outcome));
    }

    private void OpenPagedShrineList<TValue>(
        string title,
        IReadOnlyList<TValue> choices,
        Func<TValue, string> getLabel,
        Action<TValue> onSelect,
        int pageIndex = 0)
        where TValue : class
    {
        const int pageSize = 10;
        if (choices == null || choices.Count == 0)
            return;

        var pageCount = Math.Max(1, (choices.Count + pageSize - 1) / pageSize);
        pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
        var firstIndex = pageIndex * pageSize;
        var lastIndex = Math.Min(firstIndex + pageSize, choices.Count);
        var entries = new List<ShrinePagedDialogEntry<TValue>>(pageSize + 2);
        if (pageCount > 1)
        {
            var previousPage = pageIndex == 0 ? pageCount - 1 : pageIndex - 1;
            entries.Add(new ShrinePagedDialogEntry<TValue>(T("上一页", "Previous page"), targetPage: previousPage));
        }
        for (var i = firstIndex; i < lastIndex; i++)
        {
            var value = choices[i];
            entries.Add(new ShrinePagedDialogEntry<TValue>(getLabel(value), value));
        }
        if (pageCount > 1)
        {
            var nextPage = pageIndex + 1 >= pageCount ? 0 : pageIndex + 1;
            entries.Add(new ShrinePagedDialogEntry<TValue>(T("下一页", "Next page"), targetPage: nextPage));
        }

        var pageTitle = pageCount > 1
            ? title + " (" + (pageIndex + 1) + "/" + pageCount + ")"
            : title;
        Dialog.List(
            pageTitle,
            entries,
            entry => entry.Label,
            (entryIndex, _) =>
            {
                if (entryIndex < 0 || entryIndex >= entries.Count)
                    return true;
                ShrinePagedDialogEntry<TValue> entry = entries[entryIndex];
                if (entry.TargetPage >= 0)
                {
                    OpenPagedShrineList<TValue>(title, choices, getLabel, onSelect, entry.TargetPage);
                    return true;
                }
                if (entry.Value != null)
                    onSelect(entry.Value);
                return true;
            },
            true);
    }

    private List<ShrineEffectOutcome> BuildShrineEffectOutcomes(
        TraitShrine shrine,
        ShrineData selected)
    {
        try
        {
            switch (selected?.id)
            {
                case "material":
                    return BuildMaterialShrineOutcomes(shrine?.owner?.LV ?? 0);
                case "armor":
                    return BuildArmorShrineOutcomes();
                case "knowledge":
                    return BuildKnowledgeShrineOutcomes();
                case "invention":
                    return BuildInventionShrineOutcomes();
                default:
                    return new List<ShrineEffectOutcome>();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Shrine effect outcome list failed: " + ex.Message);
            return new List<ShrineEffectOutcome>();
        }
    }

    private static List<ShrineEffectOutcome> BuildMaterialShrineOutcomes(int shrineLevel)
    {
        var outcomes = new List<ShrineEffectOutcome>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tierMap = SourceMaterial.tierMap;
        if (tierMap == null || tierMap.Count == 0)
            return outcomes;

        var materialLevel = Math.Max(0, shrineLevel / 3);
        var minimumTier = materialLevel < 25 ? 0 : materialLevel < 60 ? 1 : 2;
        var maximumTier = Math.Min(4, materialLevel / 10 + 1);
        if (maximumTier < minimumTier)
            maximumTier = minimumTier;

        foreach (var group in new[] { "metal", "leather" })
        {
            if (!tierMap.TryGetValue(group, out var tierList) || tierList?.tiers == null)
                continue;

            var lastTier = Math.Min(maximumTier, tierList.tiers.Length - 1);
            for (var tierIndex = minimumTier; tierIndex <= lastTier; tierIndex++)
            {
                var tier = tierList.tiers[tierIndex];
                if (tier?.list == null)
                    continue;

                foreach (var material in tier.list)
                {
                    if (material == null || (tier.sum > 0 && material.chance <= 0))
                        continue;
                    var key = string.IsNullOrEmpty(material.alias)
                        ? material.id.ToString()
                        : material.alias;
                    if (!seen.Add(key))
                        continue;
                    outcomes.Add(new ShrineEffectOutcome(GetShrineMaterialLabel(material), material));
                }
            }
        }

        SortShrineEffectOutcomes(outcomes);
        return outcomes;
    }

    private static List<ShrineEffectOutcome> BuildArmorShrineOutcomes()
    {
        var outcomes = new List<ShrineEffectOutcome>();
        var aliases = GameAccess.Sources.Materials?.alias;
        if (aliases == null)
            return outcomes;

        foreach (var alias in new[] { "granite", "gold" })
        {
            if (aliases.TryGetValue(alias, out var material) && material != null)
                outcomes.Add(new ShrineEffectOutcome(GetShrineMaterialLabel(material), material));
        }
        return outcomes;
    }

    private static List<ShrineEffectOutcome> BuildKnowledgeShrineOutcomes()
    {
        var outcomes = new List<ShrineEffectOutcome>
        {
            new(GetShrineThingLabel("book_ancient"), itemId: "book_ancient"),
            new(GetShrineThingLabel("book_skill"), itemId: "book_skill")
        };
        SortShrineEffectOutcomes(outcomes);
        return outcomes;
    }

    private static List<ShrineEffectOutcome> BuildInventionShrineOutcomes()
    {
        var outcomes = new List<ShrineEffectOutcome>();
        var manager = GameAccess.Runtime.Player?.recipes;
        var pc = GameAccess.Characters.PlayerCharacter;
        if (manager == null || pc == null)
            return outcomes;

        RecipeManager.BuildList();
        var eligible = new List<RecipeSource>();
        foreach (var source in RecipeManager.list)
        {
            if (source == null || source.row == null || source.alwaysKnown || source.noRandomRecipe ||
                (!source.NeedFactory && !source.IsQuickCraft) ||
                manager.knownRecipes.ContainsKey(source.id) ||
                source.row.ContainsTag("hiddenRecipe"))
                continue;

            var requiredSkill = source.GetReqSkill();
            if (requiredSkill != null && pc.Evalue(requiredSkill.id) + 15 >= source.row.LV)
                eligible.Add(source);
        }

        if (eligible.Count == 0)
        {
            foreach (var recipeId in manager.knownRecipes.Keys)
            {
                var source = RecipeManager.Get(recipeId);
                if (source?.row is SourceThing.Row && !source.noRandomRecipe &&
                    (source.NeedFactory || source.IsQuickCraft))
                    eligible.Add(source);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in eligible)
        {
            if (string.IsNullOrEmpty(source.id) || !seen.Add(source.id))
                continue;
            outcomes.Add(new ShrineEffectOutcome(GetShrineRecipeLabel(source), recipeId: source.id));
        }
        SortShrineEffectOutcomes(outcomes);
        return outcomes;
    }

    private static string GetShrineMaterialLabel(SourceMaterial.Row material)
    {
        var name = GetMaterialDisplayName(material);
        if (string.IsNullOrWhiteSpace(name))
            name = material.alias;
        return string.IsNullOrWhiteSpace(material.alias) || string.Equals(name, material.alias, StringComparison.Ordinal)
            ? name
            : name + " [" + material.alias + "]";
    }

    private static string GetShrineThingLabel(string id)
    {
        try
        {
            if (GameAccess.Sources.Things?.map != null && GameAccess.Sources.Things.map.TryGetValue(id, out var row) && row != null)
            {
                var name = row.GetName();
                if (!string.IsNullOrWhiteSpace(name))
                    return name + " [" + id + "]";
            }
        }
        catch { }
        return id;
    }

    private static string GetShrineRecipeLabel(RecipeSource source)
    {
        string name;
        try { name = source.Name; }
        catch { name = ""; }
        if (string.IsNullOrWhiteSpace(name))
            name = source.id;
        return string.Equals(name, source.id, StringComparison.Ordinal)
            ? name
            : name + " [" + source.id + "]";
    }

    private static void SortShrineEffectOutcomes(List<ShrineEffectOutcome> outcomes)
    {
        outcomes.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.CurrentCulture));
    }

    private static void ExecuteSelectedShrineEffect(
        TraitShrine shrine,
        Chara user,
        ShrineData selected,
        ShrineEffectOutcome? outcome)
    {
        if (shrine == null || shrine.owner == null || !shrine.owner.isOn || selected == null)
            return;

        var registered = GameAccess.Runtime.GameData?.shrines;
        var selectedIndex = registered?.IndexOf(selected) ?? -1;
        if (selectedIndex < 0)
            return;

        shrine.owner.refVal = selectedIndex;
        shrine.owner.idSkin = selected.skin;
        shrine.mat = outcome?.Material;
        if (shrine.mat == null)
            shrine.GetMaterial();

        try
        {
            _selectedShrineEffectOutcome = outcome;
            _shrineEffectSelectionExecuting = true;
            shrine.OnUse(user ?? GameAccess.Characters.PlayerCharacter);
        }
        finally
        {
            _shrineEffectSelectionExecuting = false;
            _selectedShrineEffectOutcome = null;
        }
    }

    [HarmonyPatch(typeof(TraitPowerStatue), "OnUse", new[] { typeof(Chara) })]
    private static class TraitPowerStatueShrineEffectSelectionPatch
    {
        private static bool Prefix(TraitPowerStatue __instance, Chara __0, ref bool __result)
        {
            var instance = Instance;
            if (_shrineEffectSelectionExecuting || instance == null || !instance._shrineEffectSelection ||
                !(__instance is TraitShrine shrine))
                return true;

            if (!instance.TryOpenShrineEffectSelection(shrine, __0))
                return true;

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TraitShrine), "_OnUse", new[] { typeof(Chara) })]
    private static class TraitShrineSelectedOutcomePatch
    {
        private static bool Prefix(TraitShrine __instance)
        {
            var outcome = _selectedShrineEffectOutcome;
            if (!_shrineEffectSelectionExecuting || outcome == null || __instance?.owner == null)
                return true;

            var shrineId = __instance.Shrine?.id;
            if (string.Equals(shrineId, "knowledge", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(outcome.ItemId))
            {
                var point = __instance.owner.ExistsOnMap ? __instance.owner.pos : GameAccess.Characters.PlayerCharacter.pos;
                var thing = GameAccess.Spawn.CreateThing(outcome.ItemId, -1, __instance.owner.LV);
                GameAccess.World.AddCard(GameAccess.World.CurrentZone, thing, point);
                return false;
            }

            if (string.Equals(shrineId, "invention", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(outcome.RecipeId) && GameAccess.Runtime.Player?.recipes != null)
            {
                GameAccess.Messages.Say("learnRecipeIdea");
                GameAccess.Runtime.Player.recipes.Add(outcome.RecipeId, true);
                return false;
            }

            return true;
        }
    }
}
