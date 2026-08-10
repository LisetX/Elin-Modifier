using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

internal sealed partial class AiInstructionModule
{
    private void BindAbilitySelectionButton(Chara actor, UIButton button, AbilityChoice choice)
    {
        if (button == null)
            return;
        try
        {
            if (button.icon != null)
            {
                choice.Ability.SetImage(button.icon);
                if (button.icon.sprite == null)
                    button.icon.sprite = choice.Ability.GetSprite();
                button.icon.preserveAspect = true;
                button.icon.gameObject.SetActive(button.icon.sprite != null);
            }
        }
        catch
        {
        }
        try
        {
            var target = button.GetComponent<EmTooltipTarget>();
            if (target == null)
                target = button.gameObject.AddComponent<EmTooltipTarget>();
            target.Initialize(
                BuildAbilityTooltip(actor, choice),
                button.mainText?.font,
                ResolveAbilityTooltipVisualStyle);
        }
        catch
        {
        }
    }

    private EmTooltipVisualStyle ResolveAbilityTooltipVisualStyle()
    {
        if (!_host.ModuleUiRoundedCorners)
            return default;
        return new EmTooltipVisualStyle(true, _host.GetModuleStandardRoundedSprite());
    }

    private EmTooltipContent BuildAbilityTooltip(Chara actor, AbilityChoice choice)
    {
        var ability = choice.Ability;
        var source = ability.source;
        Element? element = null;
        try { element = actor.elements.GetOrCreateElement(ability.id); }
        catch
        {
        }

        var title = SafeAbilityTooltipText(() => element?.FullName, choice.Name);
        var description = SafeAbilityTooltipText(() => source?.GetDetail(), T("未提供描述", "No description available"));
        Sprite? icon = null;
        try { icon = ability.GetSprite(); }
        catch
        {
            try { icon = source?.GetSprite(); }
            catch { }
        }
        var content = new EmTooltipContent(title, icon, description);
        var displayLevel = SafeAbilityTooltipInt(() => element?.DisplayValue ?? choice.Level, choice.Level);
        var baseLevel = SafeAbilityTooltipInt(() => element?.ValueWithoutLink ?? choice.Level, choice.Level);
        var target = SafeAbilityTooltipText(
            () => ability.TargetType.ToString().lang(),
            ability.TargetType.ToString());
        var chance = "-";
        if (element is Spell)
        {
            var value = SafeAbilityTooltipInt(() => actor.CalcCastingChance(element, 1), 0);
            chance = Mathf.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture) + "%";
        }
        content.Lines.Add(new EmTooltipLine(
            T("等级", "Level") + " " + displayLevel.ToString(CultureInfo.InvariantCulture) +
            " (" + T("基础等级", "Base level") + " " + baseLevel.ToString(CultureInfo.InvariantCulture) + ")    " +
            T("目标", "Target") + " " + target + "    " +
            T("成功率", "Success rate") + " " + chance));

        var power = element == null ? 0 : SafeAbilityTooltipInt(() => element.GetPower(actor), 0);
        var related = ResolveRelatedAbility(source);
        if (related.Row != null)
        {
            var relatedText = T("关联能力", "Related ability") + "  " + related.Name;
            if (source != null && source.lvFactor > 0)
                relatedText += "    " + T("威力", "Power") + " " + power.ToString(CultureInfo.InvariantCulture);
            content.Lines.Add(new EmTooltipLine(relatedText, related.Icon));
        }
        else if (source != null && source.lvFactor > 0)
        {
            content.Lines.Add(new EmTooltipLine(
                T("威力", "Power") + " " + power.ToString(CultureInfo.InvariantCulture)));
        }

        if (element != null)
        {
            var notes = BuildAbilityTooltipNotes(actor, ability, element, source, power, related.Name);
            for (var i = 0; i < notes.Count; i++)
                content.Lines.Add(new EmTooltipLine("• " + notes[i], null, new Color(0.82f, 0.84f, 0.87f, 1f)));
            AddAbilityCostLine(content, actor, element);
        }
        return content;
    }

    private List<string> BuildAbilityTooltipNotes(
        Chara actor,
        Act ability,
        Element element,
        SourceElement.Row? source,
        int power,
        string relatedName)
    {
        var notes = new List<string>();
        var extra = SafeAbilityTooltipText(() => source?.GetText("textExtra", false), "");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            var entries = extra.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length; i++)
            {
                var note = entries[i].Trim();
                if (note.Length == 0)
                    continue;
                if (note.StartsWith("@", StringComparison.Ordinal))
                {
                    var conditionName = ResolveAbilityConditionName(actor, note.Substring(1), power);
                    if (!string.IsNullOrWhiteSpace(conditionName))
                        notes.Add(conditionName);
                    continue;
                }
                note = note.Replace("#calc", power.ToString(CultureInfo.InvariantCulture))
                    .Replace("#ele", relatedName.ToLowerInvariant())
                    .Replace(';', ',');
                if (!string.IsNullOrWhiteSpace(note))
                    notes.Add(note);
            }
        }
        if (HasAbilitySourceTag(source, "syncRide"))
            AddUniqueAbilityTooltipNote(notes, SafeAbilityTooltipText(() => "hintSyncRide".lang(), ""));
        try
        {
            if (EClass.pc != null && EClass.pc.HasElement(1274) && HasAbilitySourceTag(source, "dontForget"))
                AddUniqueAbilityTooltipNote(notes, SafeAbilityTooltipText(() => "hintDontForget".lang(), ""));
        }
        catch
        {
        }
        try
        {
            if (ability.HaveLongPressAction && element.id != 8230 && element.id != 8232)
                AddUniqueAbilityTooltipNote(notes, SafeAbilityTooltipText(() => "hintPartyAbility".lang(), ""));
        }
        catch
        {
        }
        try
        {
            if (!ability.LocalAct)
                AddUniqueAbilityTooltipNote(notes, SafeAbilityTooltipText(() => "isGlobalAct".lang(), ""));
        }
        catch
        {
        }
        return notes;
    }

    private void AddAbilityCostLine(EmTooltipContent content, Chara actor, Element element)
    {
        Act.Cost cost;
        try { cost = element.GetCost(actor); }
        catch { return; }
        if (cost.type == Act.CostType.None || cost.cost <= 0)
            return;
        var adjusted = cost.cost;
        if (cost.type == Act.CostType.MP)
        {
            try
            {
                var reduction = actor.Evalue(483);
                if (reduction > 0)
                    adjusted = cost.cost * 100 / (100 + (int)Mathf.Sqrt(reduction * 10) * 3);
            }
            catch
            {
            }
        }
        var value = adjusted.ToString(CultureInfo.InvariantCulture);
        if (adjusted != cost.cost)
            value += " (" + cost.cost.ToString(CultureInfo.InvariantCulture) + ")";
        var label = cost.type == Act.CostType.MP
            ? T("玛那消耗", "MP cost")
            : T("活力消耗", "SP cost");
        Sprite? icon = null;
        try
        {
            icon = cost.type == Act.CostType.MP
                ? EClass.core.refs.icons.mana
                : EClass.core.refs.icons.stamina;
        }
        catch
        {
        }
        content.Lines.Add(new EmTooltipLine(label + "  " + value, icon, new Color(0.88f, 0.96f, 0.91f, 1f)));
    }

    private static string ResolveAbilityConditionName(Chara actor, string alias, int power)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return "";
        try
        {
            var condition = Condition.Create(alias, power, null);
            if (condition == null)
                return "";
            condition.owner = actor;
            return condition.Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static RelatedAbility ResolveRelatedAbility(SourceElement.Row? source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.aliasParent))
            return default;
        try
        {
            var elements = GameAccess.Sources.Elements;
            if (elements?.alias == null || !elements.alias.TryGetValue(source.aliasParent, out var row) || row == null)
                return default;
            return new RelatedAbility(row, row.GetName() ?? source.aliasParent, row.GetSprite());
        }
        catch
        {
            return default;
        }
    }

    private static bool HasAbilitySourceTag(SourceElement.Row? source, string tag)
    {
        try { return source?.tag != null && Array.IndexOf(source.tag, tag) >= 0; }
        catch { return false; }
    }

    private static void AddUniqueAbilityTooltipNote(List<string> notes, string note)
    {
        if (!string.IsNullOrWhiteSpace(note) && !notes.Contains(note))
            notes.Add(note);
    }

    private static string SafeAbilityTooltipText(Func<string?> getter, string fallback)
    {
        try
        {
            var value = getter();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static int SafeAbilityTooltipInt(Func<int> getter, int fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private readonly struct RelatedAbility
    {
        internal RelatedAbility(SourceElement.Row row, string name, Sprite? icon)
        {
            Row = row;
            Name = name ?? "";
            Icon = icon;
        }

        internal SourceElement.Row? Row { get; }
        internal string Name { get; }
        internal Sprite? Icon { get; }
    }
}
