using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public sealed partial class ElinModifierPlugin
{
    private static SourceThing.Row? ResolveAiQueryThing(string key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0)
            return null;
        var table = GameAccess.Sources.Things;
        if (table?.rows == null)
            return null;
        if (table.map != null && table.map.TryGetValue(key, out var exact) && exact != null)
            return exact;
        var rows = table.rows;
        var byId = rows.FirstOrDefault(row => row != null &&
            string.Equals(row.id, key, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
            return byId;
        var byName = rows.FirstOrDefault(row => row != null &&
            (string.Equals(SafeText(() => row.GetName(), ""), key, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(row.name, key, StringComparison.OrdinalIgnoreCase)));
        if (byName != null)
            return byName;
        return rows.FirstOrDefault(row => row != null && AiQueryRowMatches(row, key));
    }

    private static bool AiQueryRowMatches(SourceThing.Row row, string key)
    {
        if (row == null)
            return false;
        if ((row.id ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if ((row.name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return SafeText(() => row.GetName(), "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string AiQueryThingLabel(SourceThing.Row? row)
    {
        if (row == null)
            return "?";
        var display = SafeText(() => row.GetName(), row.name ?? "");
        if (string.IsNullOrEmpty(display))
            display = row.name ?? "";
        return string.IsNullOrEmpty(display) ? (row.id ?? "?") : display + " (" + (row.id ?? "?") + ")";
    }

    private static string AiQueryThingLabelById(string id)
    {
        var table = GameAccess.Sources.Things;
        if (table?.map != null && !string.IsNullOrEmpty(id) && table.map.TryGetValue(id, out var row) && row != null)
            return AiQueryThingLabel(row);
        return id ?? "?";
    }

    private static string AiQueryJoin(string[]? values, string separator = ", ")
    {
        if (values == null || values.Length == 0)
            return "";
        return string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
    }

    private static List<AiQueryIngredient> ParseAiQueryComponents(string[]? components)
    {
        var result = new List<AiQueryIngredient>();
        if (components == null)
            return result;
        foreach (var raw in components)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0)
                continue;
            var optional = text.StartsWith("+", StringComparison.Ordinal);
            if (optional)
                text = text.Substring(1);
            var count = 1;
            var slash = text.IndexOf('/');
            if (slash > 0)
            {
                int.TryParse(text.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                text = text.Substring(0, slash);
                if (count <= 0)
                    count = 1;
            }
            if (text.Length == 0)
                continue;
            result.Add(new AiQueryIngredient(text, count, optional));
        }
        return result;
    }

    private static bool AiQueryComponentsContain(string[]? components, string id)
    {
        foreach (var ingredient in ParseAiQueryComponents(components))
        {
            if (string.Equals(ingredient.Id, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool AiQueryArrayContains(string[]? values, string id)
    {
        if (values == null || string.IsNullOrEmpty(id))
            return false;
        foreach (var value in values)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0)
                continue;
            var slash = text.IndexOf('/');
            if (slash > 0)
                text = text.Substring(0, slash);
            if (string.Equals(text, id, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void AppendAiQueryField(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        sb.AppendLine();
        sb.Append(label).Append(": ").Append(value);
    }

    private string AiToolQueryItem(string args)
    {
        var key = AiArgString(args, "item");
        var row = ResolveAiQueryThing(key);
        if (row == null)
            return "failed: item not found in game data: " + key;

        var sb = new StringBuilder("ok: item ").Append(AiQueryThingLabel(row));
        AppendAiQueryField(sb, "category", row.category);
        AppendAiQueryField(sb, "tag", AiQueryJoin(row.tag));
        AppendAiQueryField(sb, "trait", AiQueryJoin(row.trait));
        AppendAiQueryField(sb, "LV", row.LV.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "value", row.value.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "weight", row.weight.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "HP", row.HP.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "quality", row.quality.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "chance", row.chance.ToString(CultureInfo.InvariantCulture));
        AppendAiQueryField(sb, "defaultMaterial", row.defMat);
        AppendAiQueryField(sb, "tierGroup", row.tierGroup);
        AppendAiQueryField(sb, "workTag", row.workTag);
        AppendAiQueryField(sb, "filter", AiQueryJoin(row.filter));
        AppendAiQueryField(sb, "elements", AiQueryElementPairs(row.elements));

        var category = ResolveAiQueryCategory(row.category);
        if (category != null)
        {
            AppendAiQueryField(sb, "categorySkill", AiQueryElementName(category.skill));
            AppendAiQueryField(sb, "categoryMaxStack", category.maxStack.ToString(CultureInfo.InvariantCulture));
            AppendAiQueryField(sb, "categoryFlags", AiQueryCategoryFlags(category));
        }

        var ingredients = ParseAiQueryComponents(row.components);
        if (ingredients.Count > 0)
        {
            sb.AppendLine();
            sb.Append("craftMaterials:");
            foreach (var ingredient in ingredients)
            {
                sb.AppendLine();
                sb.Append("  ").Append(AiQueryThingLabelById(ingredient.Id))
                    .Append(" x").Append(ingredient.Count.ToString(CultureInfo.InvariantCulture));
                if (ingredient.Optional)
                    sb.Append(" (optional)");
            }
        }
        AppendAiQueryField(sb, "workbench", AiQueryJoin(row.factory));
        AppendAiQueryField(sb, "recipeKey", AiQueryJoin(row.recipeKey));
        if (row.disassemble != null && row.disassemble.Length > 0)
            AppendAiQueryField(sb, "dismantleYields", AiQueryJoin(row.disassemble));
        AppendAiQueryField(sb, "detail", SafeText(() => row.GetDetail(), row.detail ?? ""));
        return sb.ToString();
    }

    private static SourceCategory.Row? ResolveAiQueryCategory(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var table = GameAccess.Sources.Categories;
        if (table?.map == null)
            return null;
        return table.map.TryGetValue(id, out var row) ? row : null;
    }

    private static string AiQueryCategoryFlags(SourceCategory.Row row)
    {
        var flags = new List<string>();
        if (row.gift != 0) flags.Add("gift");
        if (row.deliver != 0) flags.Add("deliver");
        if (row.offer != 0) flags.Add("offer");
        if (row.ticket != 0) flags.Add("ticket");
        var recycle = AiQueryJoin(row.recycle);
        if (recycle.Length > 0) flags.Add("recycle=" + recycle);
        if (row.costSP != 0) flags.Add("costSP=" + row.costSP.ToString(CultureInfo.InvariantCulture));
        return string.Join(", ", flags.ToArray());
    }

    private static string AiQueryIntList(int[]? values)
    {
        if (values == null || values.Length == 0)
            return "";
        return string.Join(", ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());
    }
    private static string AiQueryElementName(int id)
    {
        if (id == 0)
            return "";
        var table = GameAccess.Sources.Elements;
        if (table?.map != null && table.map.TryGetValue(id, out var row) && row != null)
            return SafeText(() => row.GetName(), row.name ?? "") + " (" + id.ToString(CultureInfo.InvariantCulture) + ")";
        return id.ToString(CultureInfo.InvariantCulture);
    }
    private static string AiQueryElementPairs(int[]? elements)
    {
        if (elements == null || elements.Length == 0)
            return "";
        var table = GameAccess.Sources.Elements;
        var parts = new List<string>();
        for (var i = 0; i + 1 < elements.Length; i += 2)
        {
            var id = elements[i];
            var value = elements[i + 1];
            var name = id.ToString(CultureInfo.InvariantCulture);
            if (table?.map != null && table.map.TryGetValue(id, out var element) && element != null)
                name = SafeText(() => element.GetName(), element.name ?? name);
            parts.Add(name + "=" + value.ToString(CultureInfo.InvariantCulture));
        }
        return string.Join(", ", parts.ToArray());
    }

    private string AiToolQueryObtain(string args)
    {
        var key = AiArgString(args, "item");
        var limit = Clamp(AiArgInt(args, "limit", 20), 1, 200);
        var row = ResolveAiQueryThing(key);
        if (row == null)
            return "failed: item not found in game data: " + key;

        var id = row.id ?? "";
        var sb = new StringBuilder("ok: obtain paths for ").Append(AiQueryThingLabel(row));
        var found = 0;

        var ingredients = ParseAiQueryComponents(row.components);
        if (ingredients.Count > 0)
        {
            found++;
            sb.AppendLine();
            sb.Append("[craft] workbench=").Append(AiQueryJoin(row.factory).Length == 0 ? "none" : AiQueryJoin(row.factory));
            if (row.recipeKey != null && row.recipeKey.Length > 0)
                sb.Append(" recipeKey=").Append(AiQueryJoin(row.recipeKey));
            foreach (var ingredient in ingredients)
            {
                sb.AppendLine();
                sb.Append("  need ").Append(AiQueryThingLabelById(ingredient.Id))
                    .Append(" x").Append(ingredient.Count.ToString(CultureInfo.InvariantCulture));
                if (ingredient.Optional)
                    sb.Append(" (optional)");
            }
        }

        AppendAiQueryRecipeTableSection(sb, id, ref found);
        AppendAiQueryDismantleSection(sb, id, limit, ref found);
        AppendAiQueryGatherSection(sb, id, limit, ref found);
        AppendAiQueryCharaLootSection(sb, id, limit, ref found);
        AppendAiQuerySpawnListSection(sb, row, limit, ref found);

        var category = ResolveAiQueryCategory(row.category);
        if (category != null)
        {
            var flags = AiQueryCategoryFlags(category);
            if (flags.Length > 0)
            {
                found++;
                sb.AppendLine();
                sb.Append("[category] ").Append(row.category).Append(" -> ").Append(flags);
            }
        }

        if (found == 0)
        {
            sb.AppendLine();
            sb.Append("no static acquisition path found in game data; it may come from quests, events, fixed map placement, or runtime drop logic");
        }
        return sb.ToString();
    }

    private static void AppendAiQueryRecipeTableSection(StringBuilder sb, string id, ref int found)
    {
        var rows = GameAccess.Sources.Recipes?.rows;
        if (rows == null)
            return;
        foreach (var recipe in rows)
        {
            if (recipe == null || !string.Equals(recipe.thing, id, StringComparison.OrdinalIgnoreCase))
                continue;
            found++;
            sb.AppendLine();
            sb.Append("[recipeTable] factory=").Append(recipe.factory ?? "")
                .Append(" type=").Append(recipe.type ?? "")
                .Append(" num=").Append(recipe.num ?? "")
                .Append(" sp=").Append(recipe.sp.ToString(CultureInfo.InvariantCulture));
            var ing = AiQueryJoin(recipe.ing1) + " " + AiQueryJoin(recipe.ing2) + " " + AiQueryJoin(recipe.ing3);
            if (!string.IsNullOrWhiteSpace(ing))
                sb.Append(" ing=").Append(ing.Trim());
        }
    }

    private static void AppendAiQueryDismantleSection(StringBuilder sb, string id, int limit, ref int found)
    {
        var rows = GameAccess.Sources.Things?.rows;
        if (rows == null)
            return;
        var hits = rows.Where(row => row != null && AiQueryArrayContains(row.disassemble, id)).Take(limit).ToList();
        if (hits.Count == 0)
            return;
        found++;
        sb.AppendLine();
        sb.Append("[dismantle] obtained by dismantling:");
        foreach (var hit in hits)
        {
            sb.AppendLine();
            sb.Append("  ").Append(AiQueryThingLabel(hit));
        }
    }

    private static void AppendAiQueryGatherSection(StringBuilder sb, string id, int limit, ref int found)
    {
        var rows = GameAccess.Sources.Objects?.rows;
        if (rows == null)
            return;
        var hits = rows.Where(row => row != null && AiQueryComponentsContain(row.components, id)).Take(limit).ToList();
        if (hits.Count == 0)
            return;
        found++;
        sb.AppendLine();
        sb.Append("[gather] dropped by map objects:");
        foreach (var hit in hits)
        {
            sb.AppendLine();
            sb.Append("  ").Append(SafeText(() => hit.GetName(), hit.name ?? "")).Append(" (").Append(hit.id.ToString(CultureInfo.InvariantCulture)).Append(")");
            var req = AiQueryJoin(hit.reqHarvest, "/");
            if (!string.IsNullOrEmpty(req))
                sb.Append(" reqHarvest=").Append(req);
        }
    }

    private static void AppendAiQueryCharaLootSection(StringBuilder sb, string id, int limit, ref int found)
    {
        var rows = GameAccess.Sources.Characters?.rows;
        if (rows == null)
            return;
        var hits = rows
            .Where(row => row != null &&
                (AiQueryArrayContains(row.loot, id) || AiQueryComponentsContain(row.components, id)))
            .Take(limit)
            .ToList();
        if (hits.Count == 0)
            return;
        found++;
        sb.AppendLine();
        sb.Append("[npcDrop] dropped by NPCs:");
        foreach (var hit in hits)
        {
            sb.AppendLine();
            sb.Append("  ").Append(SafeText(() => hit.GetName(), hit.name ?? "")).Append(" (").Append(hit.id ?? "").Append(")")
                .Append(" LV").Append(hit.LV.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(hit.biome))
                sb.Append(" biome=").Append(hit.biome);
        }
    }

    private static void AppendAiQuerySpawnListSection(StringBuilder sb, SourceThing.Row row, int limit, ref int found)
    {
        var rows = GameAccess.Sources.SpawnLists?.rows;
        if (rows == null)
            return;
        var id = row.id ?? "";
        var category = row.category ?? "";
        var hits = rows
            .Where(entry => entry != null &&
                (AiQueryArrayContains(entry.idCard, id) ||
                 (!string.IsNullOrEmpty(category) && AiQueryArrayContains(entry.category, category))))
            .Take(limit)
            .ToList();
        if (hits.Count == 0)
            return;
        found++;
        sb.AppendLine();
        sb.Append("[spawnList] appears in spawn lists:");
        foreach (var hit in hits)
        {
            sb.AppendLine();
            sb.Append("  ").Append(hit.id ?? "").Append(" type=").Append(hit.type ?? "");
            if (!string.IsNullOrWhiteSpace(hit.parent))
                sb.Append(" parent=").Append(hit.parent);
        }
    }

    private string AiToolQueryUses(string args)
    {
        var key = AiArgString(args, "item");
        var limit = Clamp(AiArgInt(args, "limit", 40), 1, 500);
        var row = ResolveAiQueryThing(key);
        if (row == null)
            return "failed: item not found in game data: " + key;

        var id = row.id ?? "";
        var rows = GameAccess.Sources.Things?.rows;
        if (rows == null)
            return "failed: item table unavailable";

        var hits = rows.Where(entry => entry != null && AiQueryComponentsContain(entry.components, id)).ToList();
        var sb = new StringBuilder("ok: ").Append(AiQueryThingLabel(row)).Append(" is used to craft ")
            .Append(hits.Count.ToString(CultureInfo.InvariantCulture)).Append(" item(s)");
        foreach (var hit in hits.Take(limit))
        {
            sb.AppendLine();
            sb.Append("  ").Append(AiQueryThingLabel(hit));
            var factory = AiQueryJoin(hit.factory);
            if (!string.IsNullOrEmpty(factory))
                sb.Append(" workbench=").Append(factory);
            sb.Append(" needs=").Append(AiQueryJoin(hit.components));
        }
        if (hits.Count > limit)
            sb.AppendLine().Append("truncated: raise limit to see all rows");

        if (row.disassemble != null && row.disassemble.Length > 0)
            sb.AppendLine().Append("dismantleYields: ").Append(AiQueryJoin(row.disassemble));
        return sb.ToString();
    }
}

internal sealed class AiQueryIngredient
{
    internal readonly string Id;
    internal readonly int Count;
    internal readonly bool Optional;

    internal AiQueryIngredient(string id, int count, bool optional)
    {
        Id = id;
        Count = count;
        Optional = optional;
    }
}
