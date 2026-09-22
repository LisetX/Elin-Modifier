using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public sealed partial class ElinModifierPlugin
{
    private string AiToolQueryNpc(string args)
    {
        var key = AiArgString(args, "npc");
        var rows = GameAccess.Sources.Characters?.rows;
        if (rows == null)
            return "failed: chara table unavailable";

        var row = rows.FirstOrDefault(entry => entry != null &&
                      string.Equals(entry.id, key, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault(entry => entry != null &&
                      string.Equals(SafeText(() => entry.GetName(), entry.name ?? ""), key, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault(entry => entry != null &&
                      ((entry.id ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       SafeText(() => entry.GetName(), entry.name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0));
        if (row == null)
            return "failed: npc not found in game data: " + key;

        var sb = new StringBuilder("ok: npc ")
            .Append(SafeText(() => row.GetName(), row.name ?? "")).Append(" (").Append(row.id ?? "").Append(")");
        AppendAiQueryField(sb, "race", row.race);
        AppendAiQueryField(sb, "job", row.job);
        AppendAiQueryField(sb, "LV", row.LV.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "quality", row.quality.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "chance", row.chance.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "hostility", row.hostility);
        AppendAiQueryField(sb, "biome", row.biome);
        AppendAiQueryField(sb, "category", row.category);
        AppendAiQueryField(sb, "tag", AiQueryJoin(row.tag));
        AppendAiQueryField(sb, "trait", AiQueryJoin(row.trait));
        AppendAiQueryField(sb, "filter", AiQueryJoin(row.filter));
        AppendAiQueryField(sb, "tactics", row.tactics);
        AppendAiQueryField(sb, "faith", row.faith);
        AppendAiQueryField(sb, "works", AiQueryJoin(row.works));
        AppendAiQueryField(sb, "hobbies", AiQueryJoin(row.hobbies));
        AppendAiQueryField(sb, "mainElement", AiQueryJoin(row.mainElement));
        AppendAiQueryField(sb, "elements", AiQueryElementPairs(row.elements));
        AppendAiQueryField(sb, "actCombat", AiQueryJoin(row.actCombat));
        AppendAiQueryField(sb, "equip", row.equip);
        AppendAiQueryField(sb, "recruitItems", AiQueryJoin(row.recruitItems));
        AppendAiQueryField(sb, "detail", SafeText(() => row.GetDetail(), row.detail ?? ""));

        var loot = AiQueryJoin(row.loot);
        if (!string.IsNullOrEmpty(loot))
            AppendAiQueryField(sb, "loot", loot);
        var components = AiQueryJoin(row.components);
        if (!string.IsNullOrEmpty(components))
            AppendAiQueryField(sb, "components", components);

        var spawnRows = GameAccess.Sources.SpawnLists?.rows;
        if (spawnRows != null)
        {
            var id = row.id ?? "";
            var category = row.category ?? "";
            var hits = spawnRows
                .Where(entry => entry != null &&
                    (AiQueryArrayContains(entry.idCard, id) ||
                     (!string.IsNullOrEmpty(category) && AiQueryArrayContains(entry.category, category))))
                .Take(20)
                .ToList();
            if (hits.Count > 0)
            {
                sb.AppendLine();
                sb.Append("spawnLists:");
                foreach (var hit in hits)
                {
                    sb.AppendLine();
                    sb.Append("  ").Append(hit.id ?? "").Append(" type=").Append(hit.type ?? "");
                }
            }
        }
        return sb.ToString();
    }

    private string AiToolQueryElement(string args)
    {
        var key = AiArgString(args, "element");
        var table = GameAccess.Sources.Elements;
        var rows = table?.rows;
        if (rows == null)
            return "failed: element table unavailable";

        SourceElement.Row? row = null;
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) &&
            table!.map != null && table.map.TryGetValue(numericId, out var byId))
            row = byId;
        row ??= rows.FirstOrDefault(entry => entry != null &&
                   string.Equals(entry.alias, key, StringComparison.OrdinalIgnoreCase));
        row ??= rows.FirstOrDefault(entry => entry != null &&
                   string.Equals(SafeText(() => entry.GetName(), entry.name ?? ""), key, StringComparison.OrdinalIgnoreCase));
        row ??= rows.FirstOrDefault(entry => entry != null &&
                   SafeText(() => entry.GetName(), entry.name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        if (row == null)
            return "failed: element not found in game data: " + key;

        var sb = new StringBuilder("ok: element ")
            .Append(SafeText(() => row.GetName(), row.name ?? "")).Append(" (id=")
            .Append(row.id.ToString(CultureInfo.InvariantCulture)).Append(" alias=").Append(row.alias ?? "").Append(")");
        AppendAiQueryField(sb, "type", row.type);
        AppendAiQueryField(sb, "group", row.group);
        AppendAiQueryField(sb, "category", row.category);
        AppendAiQueryField(sb, "abilityType", AiQueryJoin(row.abilityType));
        AppendAiQueryField(sb, "target", row.target);
        AppendAiQueryField(sb, "proc", AiQueryJoin(row.proc));
        AppendAiQueryField(sb, "cost", AiQueryIntList(row.cost));
        AppendAiQueryField(sb, "req", AiQueryJoin(row.req));
        AppendAiQueryField(sb, "max", row.max.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "LV", row.LV.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "geneSlot", row.geneSlot.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "encSlot", row.encSlot);
        AppendAiQueryField(sb, "cooldown", row.cooldown.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "charge", row.charge.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "radius", row.radius.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "tag", AiQueryJoin(row.tag));
        AppendAiQueryField(sb, "aliasParent", row.aliasParent);
        AppendAiQueryField(sb, "aliasRef", row.aliasRef);
        AppendAiQueryField(sb, "idTrainer", row.idTrainer);
        AppendAiQueryField(sb, "tagTrainer", row.tagTrainer);
        AppendAiQueryField(sb, "foodEffect", AiQueryJoin(row.foodEffect));
        AppendAiQueryField(sb, "detail", SafeText(() => row.GetDetail(), row.detail ?? ""));

        var playerValue = SafeInt(() => GameAccess.Characters.GetPlayerElementValue(row.id), int.MinValue);
        if (playerValue != int.MinValue)
            AppendAiQueryField(sb, "playerValue", playerValue.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private string AiToolQueryMaterial(string args)
    {
        var key = AiArgString(args, "material");
        var table = GameAccess.Sources.Materials;
        var rows = table?.rows;
        if (rows == null)
            return "failed: material table unavailable";

        var row = rows.FirstOrDefault(entry => entry != null &&
                      string.Equals(entry.alias, key, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault(entry => entry != null &&
                      string.Equals(SafeText(() => entry.GetName(), entry.name ?? ""), key, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault(entry => entry != null &&
                      SafeText(() => entry.GetName(), entry.name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
        if (row == null)
            return "failed: material not found in game data: " + key;

        var sb = new StringBuilder("ok: material ")
            .Append(SafeText(() => row.GetName(), row.name ?? "")).Append(" (id=")
            .Append(row.id.ToString(CultureInfo.InvariantCulture)).Append(" alias=").Append(row.alias ?? "").Append(")");
        AppendAiQueryField(sb, "hardness", row.hardness.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "tag", AiQueryJoin(row.tag));
        AppendAiQueryField(sb, "category", row.category);
        AppendAiQueryField(sb, "value", row.value.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "tier", row.tier.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "quality", row.quality.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "hardnessNote", "gathering threshold scales with this value; the hard tag triples it");

        var things = GameAccess.Sources.Things?.rows;
        if (things != null)
        {
            var hits = things.Where(entry => entry != null &&
                string.Equals(entry.defMat, row.alias, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();
            if (hits.Count > 0)
            {
                sb.AppendLine();
                sb.Append("defaultMaterialOf:");
                foreach (var hit in hits)
                {
                    sb.AppendLine();
                    sb.Append("  ").Append(AiQueryThingLabel(hit));
                }
            }
        }
        return sb.ToString();
    }

    private string AiToolQueryPlayerState(string args)
    {
        var player = GameAccess.Characters.PlayerCharacter;
        if (player == null)
            return "failed: player character unavailable";

        var sb = new StringBuilder("ok: player state");
        AppendAiQueryField(sb, "name", SafeText(() => player.Name, ""));
        AppendAiQueryField(sb, "LV", SafeInt(() => player.LV, 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "hp", SafeInt(() => player.hp, 0).ToString(CultureInfo.InvariantCulture) + "/" +
            SafeInt(() => player.MaxHP, 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "mana", SafeInt(() => player.mana.value, 0).ToString(CultureInfo.InvariantCulture) + "/" +
            SafeInt(() => player.mana.max, 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "stamina", SafeInt(() => player.stamina.value, 0).ToString(CultureInfo.InvariantCulture) + "/" +
            SafeInt(() => player.stamina.max, 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "faith", SafeText(() => player.idFaith, ""));
        AppendAiQueryField(sb, "feat", SafeInt(() => player.feat, 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "money", SafeInt(() => player.GetCurrency("money"), 0).ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "platinum", SafeInt(() => player.GetCurrency("plat"), 0).ToString(CultureInfo.InvariantCulture));

        var zone = GameAccess.World.CurrentZone;
        if (zone != null)
        {
            AppendAiQueryField(sb, "zone", SafeText(() => zone.NameWithLevel, ""));
            AppendAiQueryField(sb, "zoneLevel", SafeInt(() => zone.DangerLv, 0).ToString(CultureInfo.InvariantCulture));
        }

        var elements = GameAccess.Characters.PlayerElements;
        if (elements?.dict != null)
        {
            var skillRows = new List<string>();
            try
            {
                foreach (var element in elements.dict.Values.Where(entry => entry != null).OrderByDescending(entry => entry.Value).Take(24))
                {
                    var name = SafeText(() => element.Name, element.id.ToString(CultureInfo.InvariantCulture));
                    skillRows.Add(name + "=" + element.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch
            {
            }
            if (skillRows.Count > 0)
                AppendAiQueryField(sb, "topElements", string.Join(", ", skillRows.ToArray()));
        }

        var partySize = SafeInt(() => player.party.members.Count, -1);
        if (partySize >= 0)
            AppendAiQueryField(sb, "partySize", partySize.ToString(CultureInfo.InvariantCulture));

        var branch = GameAccess.World.BranchOrHomeBranch;
        if (branch != null)
        {
            AppendAiQueryField(sb, "homeLevel", SafeInt(() => branch.lv, 0).ToString(CultureInfo.InvariantCulture));
            var residents = SafeInt(() => branch.members.Count, -1);
            if (residents >= 0)
                AppendAiQueryField(sb, "homeResidents", residents.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
