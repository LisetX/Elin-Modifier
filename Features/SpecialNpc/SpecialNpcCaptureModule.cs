using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

internal sealed class SpecialNpcCaptureModule
{
    internal bool IgnoreRestriction { get; private set; }

    internal void Load(bool enabled)
    {
        IgnoreRestriction = enabled;
    }

    internal void Reset()
    {
        IgnoreRestriction = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (IgnoreRestriction == enabled)
            return false;
        IgnoreRestriction = enabled;
        return true;
    }

    internal bool CanCapture(Chara target)
    {
        if (!IgnoreRestriction || target == null)
            return false;

        var source = target.source;
        return source != null && source.quality == 4;
    }
}

internal static class SpecialNpcCapturePatchContext
{
    internal static SpecialNpcCaptureModule? Current =>
        ElinModifierPlugin.ActiveModules?.SpecialNpcCapture;
}

[HarmonyPatch(
    typeof(ActThrow),
    "Throw",
    new[]
    {
        typeof(Card), typeof(Point), typeof(Card), typeof(Thing), typeof(ThrowMethod)
    })]
internal static class ActThrowSpecialNpcCapturePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var canBeTamedGetter = AccessTools.PropertyGetter(
            typeof(TraitChara),
            "CanBeTamed");
        var helper = AccessTools.Method(
            typeof(ActThrowSpecialNpcCapturePatch),
            nameof(CanBeCapturedByMonsterBall));
        var patched = false;

        for (var i = 0; i < codes.Count; i++)
        {
            if ((codes[i].opcode != OpCodes.Callvirt && codes[i].opcode != OpCodes.Call) ||
                !Equals(codes[i].operand, canBeTamedGetter))
                continue;

            codes[i].opcode = OpCodes.Call;
            codes[i].operand = helper;
            patched = true;
            break;
        }

        if (!patched)
            throw new InvalidOperationException(
                "ActThrow.Throw monster ball tame restriction was not found.");

        return codes;
    }

    private static bool CanBeCapturedByMonsterBall(TraitChara trait)
    {
        if (trait == null)
            return false;
        if (trait.CanBeTamed)
            return true;

        var module = SpecialNpcCapturePatchContext.Current;
        return module != null && module.CanCapture(trait.owner);
    }
}
