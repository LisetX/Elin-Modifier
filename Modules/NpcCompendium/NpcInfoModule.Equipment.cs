using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

internal sealed partial class NpcInfoModule
{

    private sealed class RawIlInstruction
    {
        internal int Offset;
        internal OpCode OpCode;
        internal object? Operand;
    }

    private static readonly Dictionary<short, OpCode> RawIlOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static readonly Lazy<Dictionary<string, HashSet<string>>> FixedNpcEquipmentMap =
        new Lazy<Dictionary<string, HashSet<string>>>(BuildFixedNpcEquipmentMap);
    private static readonly Lazy<Dictionary<Type, HashSet<string>>> FixedTraitEquipmentMap =
        new Lazy<Dictionary<Type, HashSet<string>>>(BuildFixedTraitEquipmentMap);

    private static void PopulateNpcFixedEquipment(Chara template, NpcRecord npc, NpcTemplateInfo result)
    {
        var fixedItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (FixedNpcEquipmentMap.Value.TryGetValue(npc.Id, out var npcEquipment))
            fixedItemIds.UnionWith(npcEquipment);
        try
        {
            var trait = template.trait;
            if (trait != null)
            {
                foreach (var pair in FixedTraitEquipmentMap.Value)
                {
                    if (pair.Key.IsInstanceOfType(trait))
                        fixedItemIds.UnionWith(pair.Value);
                }
            }
        }
        catch
        {
        }
        PopulateNpcBodySlots(template, result);
        if (fixedItemIds.Count == 0)
            return;

        var seen = new List<Thing>();
        var slots = template.body?.slots;
        if (slots != null)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var item = slot?.thing;
                if (item == null || string.IsNullOrWhiteSpace(item.id) ||
                    !fixedItemIds.Contains(item.id))
                    continue;
                if (seen.Any(candidate => ReferenceEquals(candidate, item)))
                    continue;

                var slotName = "";
                try { slotName = slot.name; } catch { }
                AddNpcFixedEquipmentEntry(
                    result,
                    item,
                    slotName,
                    false,
                    false,
                    seen);
            }
        }

        if (template.things == null)
            return;
        for (var i = 0; i < template.things.Count; i++)
        {
            var item = template.things[i];
            if (item == null || string.IsNullOrWhiteSpace(item.id) ||
                !fixedItemIds.Contains(item.id) ||
                seen.Any(candidate => ReferenceEquals(candidate, item)))
                continue;
            var isEquipmentOrRanged = false;
            var isRanged = false;
            try
            {
                isEquipmentOrRanged = item.IsEquipmentOrRanged;
                isRanged = item.IsRangedWeapon;
            }
            catch
            {
            }
            if (!isEquipmentOrRanged)
                continue;
            AddNpcFixedEquipmentEntry(
                result,
                item,
                "",
                isRanged,
                true,
                seen);
        }
    }

    private static void PopulateNpcBodySlots(
        Chara template,
        NpcTemplateInfo result)
    {
        var slots = template.body?.slots;
        if (slots == null)
            return;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.elementId == 0 || slot.elementId == 44)
                continue;
            SourceElement.Row element;
            try { element = slot.element; }
            catch { continue; }
            if (element == null)
                continue;
            var name = "";
            try { name = slot.name; } catch { }
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = element.GetName(); } catch { }
            }
            result.BodySlots.Add(new NpcBodySlotEntry
            {
                ElementId = slot.elementId,
                Index = i,
                Name = string.IsNullOrWhiteSpace(name) ? element.alias ?? slot.elementId.ToString(CultureInfo.InvariantCulture) : name,
                Element = element
            });
        }
        var duplicateGroups = result.BodySlots
            .GroupBy(entry => entry.ElementId)
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            var partIndex = 1;
            foreach (var entry in group)
            {
                entry.Name += " " + partIndex.ToString(CultureInfo.InvariantCulture);
                partIndex++;
            }
        }
    }

    private static void AddNpcFixedEquipmentEntry(
        NpcTemplateInfo result,
        Thing item,
        string slotName,
        bool isRanged,
        bool isCarried,
        ICollection<Thing> seen)
    {
        seen.Add(item);
        var name = "";
        try { name = item.Name; } catch { }
        var quantity = 1;
        try { quantity = Math.Max(1, item.Num); } catch { }
        result.Equipment.Add(new NpcEquipmentEntry
        {
            Id = item.id,
            Name = string.IsNullOrWhiteSpace(name) ? item.id : name,
            SlotName = slotName ?? "",
            IsRanged = isRanged,
            IsCarried = isCarried,
            Quantity = quantity,
            Item = item
        });
    }

    private static Dictionary<string, HashSet<string>> BuildFixedNpcEquipmentMap()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var restockEquip = typeof(Chara).GetMethod(
                "RestockEquip",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            var equipById = typeof(Chara).GetMethod(
                "EQ_ID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int), typeof(Rarity) },
                null);
            var createThing = typeof(ThingGen).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int), typeof(int) },
                null);
            var addThingById = typeof(Card).GetMethod(
                "AddThing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            var equipItemById = typeof(Chara).GetMethod(
                "EQ_Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            if (restockEquip == null)
                return result;

            var codes = ReadRawIl(restockEquip);
            var equality = typeof(string).GetMethod(
                "op_Equality",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            if (equality == null || !TryFindNpcIdLocalIndex(codes, out var npcIdLocalIndex))
                return result;
            var targetGroups = new List<Dictionary<int, List<string>>>();
            var targets = new Dictionary<int, List<string>>();
            targetGroups.Add(targets);
            for (var i = 1; i + 2 < codes.Count; i++)
            {
                if (codes[i].OpCode != OpCodes.Ldstr || !(codes[i].Operand is string npcId) ||
                    !TryGetLoadedLocalIndex(codes[i - 1], out var loadedLocalIndex) ||
                    loadedLocalIndex != npcIdLocalIndex || !CallsMethod(codes[i + 1], equality) ||
                    (codes[i + 2].OpCode != OpCodes.Brtrue && codes[i + 2].OpCode != OpCodes.Brtrue_S) ||
                    !(codes[i + 2].Operand is int target))
                    continue;
                if (targets.Count > 0 && codes[i].Offset >= targets.Keys.Min())
                {
                    targets = new Dictionary<int, List<string>>();
                    targetGroups.Add(targets);
                }
                if (!targets.TryGetValue(target, out var ids))
                {
                    ids = new List<string>();
                    targets[target] = ids;
                }
                if (!ids.Contains(npcId, StringComparer.OrdinalIgnoreCase))
                    ids.Add(npcId);
            }

            for (var groupIndex = 0; groupIndex < targetGroups.Count; groupIndex++)
            {
                targets = targetGroups[groupIndex];
                if (targets.Count == 0)
                    continue;
                var orderedTargetOffsets = targets.Keys.OrderBy(offset => offset).ToList();
                var switchExitOffset = FindNpcEquipmentSwitchExitOffset(codes, orderedTargetOffsets);
                for (var targetIndex = 0; targetIndex < orderedTargetOffsets.Count; targetIndex++)
                {
                    var targetOffset = orderedTargetOffsets[targetIndex];
                    var start = codes.FindIndex(code => code.Offset == targetOffset);
                    if (start < 0)
                        continue;
                    var endOffset = targetIndex + 1 < orderedTargetOffsets.Count
                        ? orderedTargetOffsets[targetIndex + 1]
                        : switchExitOffset;
                    var fixedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = start; i < codes.Count && codes[i].Offset < endOffset; i++)
                    {
                        var code = codes[i];
                        if ((CallsMethod(code, equipById) || CallsMethod(code, createThing) ||
                             CallsMethod(code, addThingById) || CallsMethod(code, equipItemById)) &&
                            TryReadLiteralStringArgument(codes, i, out var itemId))
                            fixedItems.Add(itemId);
                    }
                    if (fixedItems.Count == 0)
                        continue;
                    var npcIds = targets[targetOffset];
                    for (var i = 0; i < npcIds.Count; i++)
                        result[npcIds[i]] = new HashSet<string>(fixedItems, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
        }
        return result;
    }

    private static Dictionary<Type, HashSet<string>> BuildFixedTraitEquipmentMap()
    {
        var result = new Dictionary<Type, HashSet<string>>();
        try
        {
            var restockEquip = typeof(Chara).GetMethod(
                "RestockEquip",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            if (restockEquip == null)
                return result;

            var equipById = typeof(Chara).GetMethod(
                "EQ_ID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int), typeof(Rarity) },
                null);
            var createThing = typeof(ThingGen).GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int), typeof(int) },
                null);
            var addThingById = typeof(Card).GetMethod(
                "AddThing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            var equipItemById = typeof(Chara).GetMethod(
                "EQ_Item",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null);
            var codes = ReadRawIl(restockEquip);
            for (var i = 0; i + 1 < codes.Count; i++)
            {
                if (codes[i].OpCode != OpCodes.Isinst || !(codes[i].Operand is Type traitType) ||
                    !typeof(Trait).IsAssignableFrom(traitType) ||
                    (codes[i + 1].OpCode != OpCodes.Brfalse && codes[i + 1].OpCode != OpCodes.Brfalse_S) ||
                    !(codes[i + 1].Operand is int endOffset))
                    continue;

                var fixedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var j = i + 2; j < codes.Count && codes[j].Offset < endOffset; j++)
                {
                    var code = codes[j];
                    if (CallsRandomMethod(code))
                        break;
                    if ((CallsMethod(code, equipById) || CallsMethod(code, createThing) ||
                         CallsMethod(code, addThingById) || CallsMethod(code, equipItemById)) &&
                        TryReadLiteralStringArgument(codes, j, out var itemId))
                        fixedItems.Add(itemId);
                }
                if (fixedItems.Count == 0)
                    continue;
                if (!result.TryGetValue(traitType, out var existing))
                {
                    existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[traitType] = existing;
                }
                existing.UnionWith(fixedItems);
            }
        }
        catch
        {
        }
        return result;
    }

    private static bool CallsRandomMethod(RawIlInstruction code)
    {
        if (!(code.Operand is MethodBase method) || method.DeclaringType != typeof(EClass))
            return false;
        return method.Name.StartsWith("rnd", StringComparison.OrdinalIgnoreCase) ||
               method.Name.StartsWith("Random", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindNpcEquipmentSwitchExitOffset(
        IReadOnlyList<RawIlInstruction> codes,
        IReadOnlyList<int> orderedTargetOffsets)
    {
        if (orderedTargetOffsets.Count == 0)
            return int.MaxValue;

        var firstTarget = orderedTargetOffsets[0];
        var lastTarget = orderedTargetOffsets[orderedTargetOffsets.Count - 1];
        var candidates = new Dictionary<int, int>();
        for (var i = 0; i < codes.Count && codes[i].Offset < firstTarget; i++)
        {
            if ((codes[i].OpCode.FlowControl != FlowControl.Branch &&
                 codes[i].OpCode.FlowControl != FlowControl.Cond_Branch) ||
                !(codes[i].Operand is int target) || target <= lastTarget)
                continue;
            candidates.TryGetValue(target, out var count);
            candidates[target] = count + 1;
        }
        if (candidates.Count > 0)
            return candidates
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First()
                .Key;

        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].Offset > lastTarget && codes[i].OpCode.FlowControl == FlowControl.Return)
                return codes[i].Offset + 1;
        }
        return int.MaxValue;
    }

    private static List<RawIlInstruction> ReadRawIl(MethodBase method)
    {
        var result = new List<RawIlInstruction>();
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes == null || bytes.Length == 0)
            return result;

        var module = method.Module;
        var typeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < bytes.Length)
        {
            var offset = position;
            var first = bytes[position++];
            short opCodeValue;
            if (first == 0xFE)
            {
                if (position >= bytes.Length)
                    break;
                opCodeValue = unchecked((short)(0xFE00 | bytes[position++]));
            }
            else
            {
                opCodeValue = first;
            }
            if (!RawIlOpCodes.TryGetValue(opCodeValue, out var opCode))
                break;

            object? operand = null;
            try
            {
                switch (opCode.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineI:
                        operand = opCode == OpCodes.Ldc_I4_S
                            ? (object)unchecked((sbyte)bytes[position])
                            : bytes[position];
                        position += 1;
                        break;
                    case OperandType.InlineI:
                        operand = BitConverter.ToInt32(bytes, position);
                        position += 4;
                        break;
                    case OperandType.InlineI8:
                        operand = BitConverter.ToInt64(bytes, position);
                        position += 8;
                        break;
                    case OperandType.ShortInlineR:
                        operand = BitConverter.ToSingle(bytes, position);
                        position += 4;
                        break;
                    case OperandType.InlineR:
                        operand = BitConverter.ToDouble(bytes, position);
                        position += 8;
                        break;
                    case OperandType.ShortInlineBrTarget:
                        {
                            var delta = unchecked((sbyte)bytes[position++]);
                            operand = position + delta;
                            break;
                        }
                    case OperandType.InlineBrTarget:
                        {
                            var delta = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = position + delta;
                            break;
                        }
                    case OperandType.InlineSwitch:
                        {
                            var count = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            var baseOffset = position + count * 4;
                            var targets = new int[count];
                            for (var i = 0; i < count; i++)
                            {
                                targets[i] = baseOffset + BitConverter.ToInt32(bytes, position);
                                position += 4;
                            }
                            operand = targets;
                            break;
                        }
                    case OperandType.InlineString:
                        {
                            var token = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = module.ResolveString(token);
                            break;
                        }
                    case OperandType.InlineMethod:
                        {
                            var token = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = module.ResolveMethod(token, typeArguments, methodArguments);
                            break;
                        }
                    case OperandType.InlineField:
                        {
                            var token = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = module.ResolveField(token, typeArguments, methodArguments);
                            break;
                        }
                    case OperandType.InlineType:
                        {
                            var token = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = module.ResolveType(token, typeArguments, methodArguments);
                            break;
                        }
                    case OperandType.InlineTok:
                        {
                            var token = BitConverter.ToInt32(bytes, position);
                            position += 4;
                            operand = module.ResolveMember(token, typeArguments, methodArguments);
                            break;
                        }
                    case OperandType.InlineSig:
                        operand = BitConverter.ToInt32(bytes, position);
                        position += 4;
                        break;
                    case OperandType.ShortInlineVar:
                        operand = bytes[position++];
                        break;
                    case OperandType.InlineVar:
                        operand = BitConverter.ToUInt16(bytes, position);
                        position += 2;
                        break;
                    default:
                        return result;
                }
            }
            catch
            {
                return result;
            }
            result.Add(new RawIlInstruction
            {
                Offset = offset,
                OpCode = opCode,
                Operand = operand
            });
        }
        return result;
    }

    private static bool TryFindNpcIdLocalIndex(
        IReadOnlyList<RawIlInstruction> codes,
        out int localIndex)
    {
        for (var i = 0; i + 1 < codes.Count; i++)
        {
            if (codes[i].OpCode != OpCodes.Ldfld || !(codes[i].Operand is FieldInfo field) ||
                !string.Equals(field.Name, "id", StringComparison.Ordinal) ||
                field.DeclaringType == null || !typeof(Card).IsAssignableFrom(field.DeclaringType) ||
                !TryGetStoredLocalIndex(codes[i + 1], out localIndex))
                continue;
            return true;
        }
        localIndex = -1;
        return false;
    }

    private static bool TryGetLoadedLocalIndex(RawIlInstruction code, out int localIndex)
    {
        if (code.OpCode == OpCodes.Ldloc_0) localIndex = 0;
        else if (code.OpCode == OpCodes.Ldloc_1) localIndex = 1;
        else if (code.OpCode == OpCodes.Ldloc_2) localIndex = 2;
        else if (code.OpCode == OpCodes.Ldloc_3) localIndex = 3;
        else if ((code.OpCode == OpCodes.Ldloc || code.OpCode == OpCodes.Ldloc_S) &&
                 TryConvertLocalOperand(code.Operand, out localIndex))
        {
        }
        else
        {
            localIndex = -1;
            return false;
        }
        return true;
    }

    private static bool TryGetStoredLocalIndex(RawIlInstruction code, out int localIndex)
    {
        if (code.OpCode == OpCodes.Stloc_0) localIndex = 0;
        else if (code.OpCode == OpCodes.Stloc_1) localIndex = 1;
        else if (code.OpCode == OpCodes.Stloc_2) localIndex = 2;
        else if (code.OpCode == OpCodes.Stloc_3) localIndex = 3;
        else if ((code.OpCode == OpCodes.Stloc || code.OpCode == OpCodes.Stloc_S) &&
                 TryConvertLocalOperand(code.Operand, out localIndex))
        {
        }
        else
        {
            localIndex = -1;
            return false;
        }
        return true;
    }

    private static bool TryConvertLocalOperand(object? operand, out int localIndex)
    {
        switch (operand)
        {
            case byte byteIndex:
                localIndex = byteIndex;
                return true;
            case ushort ushortIndex:
                localIndex = ushortIndex;
                return true;
            case int intIndex when intIndex >= 0:
                localIndex = intIndex;
                return true;
            default:
                localIndex = -1;
                return false;
        }
    }

    private static bool CallsMethod(RawIlInstruction code, MethodBase? target)
    {
        if (target == null || !(code.Operand is MethodBase method))
            return false;
        return method == target ||
               (method.Module == target.Module && method.MetadataToken == target.MetadataToken);
    }

    private static bool TryReadLiteralStringArgument(
        IReadOnlyList<RawIlInstruction> codes,
        int callIndex,
        out string value)
    {
        for (var i = callIndex - 1; i >= Math.Max(0, callIndex - 8); i--)
        {
            if (codes[i].OpCode == OpCodes.Ldstr && codes[i].Operand is string literal)
            {
                value = literal;
                return !string.IsNullOrWhiteSpace(value);
            }
            if (codes[i].OpCode.FlowControl == FlowControl.Call ||
                codes[i].OpCode.FlowControl == FlowControl.Branch ||
                codes[i].OpCode.FlowControl == FlowControl.Return)
                break;
        }
        value = "";
        return false;
    }
}
