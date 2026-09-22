using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

public sealed partial class ElinModifierPlugin
{
    private Dictionary<string, int> BuildAiQueryInventoryCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var thing in EnumerateAiInventoryThings())
            {
                if (thing == null)
                    continue;
                var id = SafeText(() => thing.id, "");
                if (string.IsNullOrEmpty(id))
                    continue;
                var amount = Math.Max(0, SafeInt(() => thing.Num, 0));
                counts.TryGetValue(id, out var current);
                counts[id] = current + amount;
            }
        }
        catch
        {
        }
        return counts;
    }

    private string AiToolQueryRecipeTree(string args)
    {
        var key = AiArgString(args, "item");
        var quantity = Math.Max(1, AiArgInt(args, "count", 1));
        var maxDepth = Clamp(AiArgInt(args, "depth", 4), 1, 8);
        var useInventory = AiArgBool(args, "check_inventory", true);
        var row = ResolveAiQueryThing(key);
        if (row == null)
            return "failed: item not found in game data: " + key;

        var ingredients = ParseAiQueryComponents(row.components);
        if (ingredients.Count == 0)
            return "ok: " + AiQueryThingLabel(row) + " has no crafting materials in game data; use query_obtain for other acquisition paths";

        var stock = useInventory ? BuildAiQueryInventoryCounts() : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var remaining = new Dictionary<string, int>(stock, StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder("ok: material tree for ").Append(AiQueryThingLabel(row))
            .Append(" x").Append(quantity.ToString(CultureInfo.InvariantCulture));
        var workbench = AiQueryJoin(row.factory);
        if (!string.IsNullOrEmpty(workbench))
            sb.Append(" workbench=").Append(workbench);

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendAiQueryTreeNodes(sb, ingredients, quantity, 1, maxDepth, remaining, totals, visiting, useInventory);

        if (totals.Count > 0)
        {
            sb.AppendLine();
            sb.Append("[shoppingList] raw materials still required:");
            foreach (var pair in totals.Where(entry => entry.Value > 0).OrderByDescending(entry => entry.Value))
            {
                sb.AppendLine();
                sb.Append("  ").Append(AiQueryThingLabelById(pair.Key)).Append(" x")
                    .Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (!totals.Any(entry => entry.Value > 0))
            {
                sb.AppendLine();
                sb.Append("  none, everything needed is already in the backpack");
            }
        }
        if (!useInventory)
            sb.AppendLine().Append("note: inventory check disabled, counts are full requirements");
        return sb.ToString();
    }

    private static void AppendAiQueryTreeNodes(
        StringBuilder sb,
        List<AiQueryIngredient> ingredients,
        int multiplier,
        int depth,
        int maxDepth,
        Dictionary<string, int> remaining,
        Dictionary<string, int> totals,
        HashSet<string> visiting,
        bool useInventory)
    {
        foreach (var ingredient in ingredients)
        {
            var need = ingredient.Count * multiplier;
            var indent = new string(' ', depth * 2);
            var have = 0;
            if (useInventory && remaining.TryGetValue(ingredient.Id, out var stocked) && stocked > 0)
            {
                have = Math.Min(stocked, need);
                remaining[ingredient.Id] = stocked - have;
            }
            var missing = need - have;

            sb.AppendLine();
            sb.Append(indent).Append(AiQueryThingLabelById(ingredient.Id))
                .Append(" need ").Append(need.ToString(CultureInfo.InvariantCulture));
            if (useInventory)
                sb.Append(" have ").Append(have.ToString(CultureInfo.InvariantCulture))
                    .Append(" missing ").Append(missing.ToString(CultureInfo.InvariantCulture));
            if (ingredient.Optional)
                sb.Append(" (optional)");

            if (missing <= 0)
                continue;

            var child = ResolveAiQueryThing(ingredient.Id);
            var childIngredients = child == null
                ? new List<AiQueryIngredient>()
                : ParseAiQueryComponents(child.components);

            if (childIngredients.Count == 0 || depth >= maxDepth || !visiting.Add(ingredient.Id))
            {
                if (childIngredients.Count > 0 && depth >= maxDepth)
                    sb.Append(" [depth limit, craftable]");
                else if (childIngredients.Count > 0)
                    sb.Append(" [cycle]");
                totals.TryGetValue(ingredient.Id, out var current);
                totals[ingredient.Id] = current + missing;
                continue;
            }

            var childWorkbench = child == null ? "" : AiQueryJoin(child.factory);
            if (!string.IsNullOrEmpty(childWorkbench))
                sb.Append(" workbench=").Append(childWorkbench);
            AppendAiQueryTreeNodes(sb, childIngredients, missing, depth + 1, maxDepth, remaining, totals, visiting, useInventory);
            visiting.Remove(ingredient.Id);
        }
    }
}
