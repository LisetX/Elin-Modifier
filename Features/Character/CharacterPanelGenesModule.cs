using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

internal sealed class CharacterPanelGenesModule
{
    private readonly IBoundGameValue<Chara> _character;

    internal CharacterPanelGenesModule(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _character = binder.BindInstanceValue<Chara>(
            typeof(WindowChara),
            GameValueAccess.Read,
            "chara");
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

    internal Chara SelectGeneHeaderCharacter(Chara original, WindowChara window)
    {
        if (!Enabled || window == null)
            return original;
        return _character.TryGet(window, out var character) && character != null
            ? character
            : original;
    }
}

internal static class CharacterPanelGenesPatchContext
{
    internal static CharacterPanelGenesModule? Current =>
        ElinModifierPlugin.ActiveModules?.CharacterPanelGenes;

    internal static bool ShouldShowGeneSection(bool original)
    {
        return original || Current?.Enabled == true;
    }

    internal static Chara SelectGeneHeaderCharacter(Chara original, WindowChara window)
    {
        return Current?.SelectGeneHeaderCharacter(original, window) ?? original;
    }
}

[HarmonyPatch(typeof(WindowChara), "RefreshSkill", new[] { typeof(int) })]
internal static class WindowCharaRefreshSkillCharacterPanelGenesPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var result = new List<CodeInstruction>(codes.Count + 5);
        var slimeEvolvableGetter = AccessTools.PropertyGetter(typeof(Card), "IsSlimeEvolvable");
        var pcGetter = AccessTools.PropertyGetter(typeof(EClass), "pc");
        var showHelper = AccessTools.Method(
            typeof(CharacterPanelGenesPatchContext),
            "ShouldShowGeneSection");
        var headerHelper = AccessTools.Method(
            typeof(CharacterPanelGenesPatchContext),
            "SelectGeneHeaderCharacter");
        if (slimeEvolvableGetter == null || showHelper == null)
            return codes;

        var geneGuardPatched = false;
        var headerCharactersPatched = 0;
        for (var i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            result.Add(code);
            if (!geneGuardPatched && code.Calls(slimeEvolvableGetter))
            {
                result.Add(new CodeInstruction(OpCodes.Call, showHelper));
                geneGuardPatched = true;
                continue;
            }

            if (!geneGuardPatched || headerCharactersPatched >= 2 ||
                pcGetter == null || headerHelper == null || !code.Calls(pcGetter))
                continue;

            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Call, headerHelper));
            headerCharactersPatched++;
        }

        return result;
    }
}
