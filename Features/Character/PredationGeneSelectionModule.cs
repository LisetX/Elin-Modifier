using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HarmonyLib;

internal sealed class PredationGeneSelectionModule
{
    private sealed class GeneCandidate
    {
        internal GeneCandidate(Thing item, string label)
        {
            Item = item;
            Label = label;
        }

        internal Thing Item { get; }
        internal string Label { get; }
    }

    private sealed class PendingSelection
    {
        internal PendingSelection(Chara actor, Chara target, Thing item)
        {
            Actor = actor;
            Target = target;
            Item = item;
        }

        internal Chara Actor { get; }
        internal Chara Target { get; }
        internal Thing Item { get; }
    }

    private static PredationGeneSelectionModule? _active;
    [ThreadStatic]
    private static bool _executingSelectedPredation;
    private readonly ElinModifierPlugin _host;
    private readonly IBoundGameMethod _makeGene;
    private readonly IBoundGameMethod _makeSlimeFood;
    private readonly IBoundGameMethod _getInvalidAction;
    private readonly IBoundGameMethod _getInvalidFeat;
    private readonly IBoundGameMethod _getBodySlot;
    private readonly IBoundGameMethod _replaceBodySlot;
    private readonly IBoundGameMethod _createElement;
    private readonly IBoundGameValue<DNA> _geneDna;
    private readonly IBoundGameValue<List<int>> _geneValues;
    private readonly IBoundGameValue<DNA.Type> _geneType;
    private PendingSelection? _pending;
    private bool _initialized;

    internal PredationGeneSelectionModule(
        ElinModifierPlugin host,
        IGameMemberBinder binder)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        _makeGene = binder.BindInstanceMethod(
            typeof(Chara),
            typeof(Thing),
            new[] { typeof(Nullable<DNA.Type>) },
            "MakeGene");
        _makeSlimeFood = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(void),
            new[] { typeof(Chara) },
            "MakeSlimeFood");
        _getInvalidAction = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(Element),
            new[] { typeof(Chara) },
            "GetInvalidAction");
        _getInvalidFeat = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(Element),
            new[] { typeof(Chara) },
            "GetInvalidFeat");
        _getBodySlot = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(int),
            Type.EmptyTypes,
            "GetBodySlot");
        _replaceBodySlot = binder.BindInstanceMethod(
            typeof(DNA),
            typeof(void),
            new[] { typeof(int) },
            "ReplaceBodySlot");
        _createElement = binder.BindStaticMethod(
            typeof(Element),
            typeof(Element),
            new[] { typeof(int), typeof(int) },
            "Create");
        _geneDna = binder.BindInstanceValue<DNA>(
            typeof(Card),
            GameValueAccess.Read,
            "c_DNA");
        _geneValues = binder.BindInstanceValue<List<int>>(
            typeof(DNA),
            GameValueAccess.Read,
            "vals");
        _geneType = binder.BindInstanceValue<DNA.Type>(
            typeof(DNA),
            GameValueAccess.Read,
            "type");
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            _pending = null;
    }

    internal void Reset()
    {
        Enabled = false;
        _pending = null;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        if (!enabled)
            _pending = null;
        return true;
    }

    internal void Initialize(HarmonyPatchModule harmonyModule, ManualLogSource logger)
    {
        if (_initialized)
            return;

        Harmony? harmony = null;
        try
        {
            EnsureBindings();
            var useAbility = AccessTools.Method(
                typeof(Chara),
                "UseAbility",
                new[] { typeof(Act), typeof(Card), typeof(Point), typeof(bool) });
            var useAbilityPrefix = AccessTools.Method(
                typeof(PredationGeneSelectionModule),
                nameof(CharaUseAbilityPrefix));
            var foodProc = AccessTools.Method(
                typeof(FoodEffect),
                "Proc",
                new[] { typeof(Chara), typeof(Thing), typeof(bool) });
            var foodProcPrefix = AccessTools.Method(
                typeof(PredationGeneSelectionModule),
                nameof(FoodEffectProcPrefix));
            var finish = AccessTools.Method(typeof(AI_Fuck), "Finish", Type.EmptyTypes);
            var finishPostfix = AccessTools.Method(
                typeof(PredationGeneSelectionModule),
                nameof(AiFuckFinishPostfix));
            if (useAbility == null || useAbilityPrefix == null || foodProc == null ||
                foodProcPrefix == null || finish == null || finishPostfix == null)
                throw new MissingMethodException("Predation gene selection patch target was not found.");

            harmony = harmonyModule.GetGroupHarmony("predation-gene-selection");
            harmony.Patch(useAbility, prefix: new HarmonyMethod(useAbilityPrefix));
            harmony.Patch(foodProc, prefix: new HarmonyMethod(foodProcPrefix));
            harmony.Patch(finish, postfix: new HarmonyMethod(finishPostfix));
            _active = this;
            _initialized = true;
        }
        catch (Exception ex)
        {
            _active = null;
            _initialized = false;
            _pending = null;
            try
            {
                harmony?.UnpatchSelf();
            }
            catch
            {
            }
            logger.LogError("Predation gene selection patch failed: " + ex);
        }
    }

    internal void Tick()
    {
        var pending = _pending;
        if (pending == null)
            return;
        if (!Enabled || !(pending.Actor.ai is AI_Fuck action) ||
            action.variation != AI_Fuck.Variation.Slime ||
            !ReferenceEquals(action.target, pending.Target))
            _pending = null;
    }

    internal void Shutdown()
    {
        if (ReferenceEquals(_active, this))
            _active = null;
        _pending = null;
        _initialized = false;
    }

    private void EnsureBindings()
    {
        if (!_makeGene.IsBound || !_makeSlimeFood.IsBound ||
            !_getInvalidAction.IsBound || !_getInvalidFeat.IsBound ||
            !_getBodySlot.IsBound || !_replaceBodySlot.IsBound ||
            !_createElement.IsBound || !_geneDna.IsBound ||
            !_geneValues.IsBound || !_geneType.IsBound)
            throw new MissingMemberException("Predation gene selection game bindings are incomplete.");
    }

    private bool TryHandleAbility(
        Chara actor,
        Act ability,
        Card targetCard,
        Point targetPoint,
        bool partyTarget)
    {
        if (!Enabled || actor == null || !actor.IsPC ||
            !(ability is ActSlime) || targetCard == null ||
            !targetCard.isChara || targetCard.Chara == null)
            return false;
        if (actor.HasCooldown(ability.id))
            return false;
        Act.SetReference(actor, targetCard, targetPoint);
        if (!ability.ValidatePerform(actor, targetCard, targetPoint))
            return true;

        var target = targetCard.Chara;
        List<GeneCandidate> candidates;
        try
        {
            candidates = BuildCandidates(actor, target);
        }
        catch
        {
            return false;
        }
        if (candidates.Count == 0)
            return false;
        if (candidates.Count == 1)
        {
            ExecuteSelectedPredation(
                actor,
                target,
                ability,
                targetPoint,
                partyTarget,
                candidates[0].Item);
            return true;
        }

        Dialog.List(
            T("选择捕食基因", "Select Devour gene"),
            candidates,
            candidate => candidate.Label,
            (index, _) =>
            {
                if (index >= 0 && index < candidates.Count)
                    ExecuteSelectedPredation(
                        actor,
                        target,
                        ability,
                        targetPoint,
                        partyTarget,
                        candidates[index].Item);
                return true;
            },
            true);
        return true;
    }

    private List<GeneCandidate> BuildCandidates(Chara actor, Chara target)
    {
        var candidates = new List<GeneCandidate>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var item = GenerateCandidate(actor, target, attempt < 3
                ? DNA.Type.Superior
                : DNA.Type.Default);
            if (item == null || !_geneDna.TryGet(item, out var dna) || dna == null)
                continue;

            _makeSlimeFood.Invoke(dna, actor);
            if (_getInvalidAction.Invoke(dna, actor) != null ||
                _getInvalidFeat.Invoke(dna, actor) != null)
                continue;

            var signature = GetCandidateSignature(dna);
            if (!signatures.Add(signature))
                continue;
            candidates.Add(new GeneCandidate(item, GetCandidateLabel(dna)));
        }
        return candidates;
    }

    private Thing? GenerateCandidate(Chara actor, Chara target, DNA.Type type)
    {
        Thing? item = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            item = GameAccessServiceHelpers.InvokeReference<Thing>(_makeGene, target, type);
            if (item == null || !_geneDna.TryGet(item, out var dna) || dna == null)
                continue;
            if (EClass.rnd(10) < actor.body.slots.Count - 2)
                break;
            if (GameAccessServiceHelpers.InvokeValue<int>(_getBodySlot, dna) == -1)
                continue;
            if (actor.body.GetSlot(35, onlyEmpty: false) == null && EClass.rnd(2) == 0)
                _replaceBodySlot.Invoke(dna, 35);
            else if (actor.body.GetSlot(32, onlyEmpty: false) == null && EClass.rnd(2) == 0)
                _replaceBodySlot.Invoke(dna, 32);
            break;
        }
        return item;
    }

    private string GetCandidateSignature(DNA dna)
    {
        var type = _geneType.TryGet(dna, out var geneType)
            ? geneType
            : DNA.Type.Default;
        var values = _geneValues.TryGet(dna, out var geneValues) && geneValues != null
            ? geneValues
            : new List<int>();
        return ((int)type).ToString(CultureInfo.InvariantCulture) + ":" +
               string.Join(",", values);
    }

    private string GetCandidateLabel(DNA dna)
    {
        var type = _geneType.TryGet(dna, out var geneType)
            ? geneType
            : DNA.Type.Default;
        var parts = new List<string> { GetGeneTypeLabel(type) };
        if (_geneValues.TryGet(dna, out var values) && values != null)
        {
            for (var i = 0; i + 1 < values.Count; i += 2)
            {
                var id = values[i];
                var value = values[i + 1];
                var element = GameAccessServiceHelpers.InvokeReference<Element>(
                    _createElement,
                    null,
                    id,
                    value);
                var name = element?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    name = id.ToString(CultureInfo.InvariantCulture);
                parts.Add(name + " " + (value >= 0 ? "+" : "") +
                          value.ToString(CultureInfo.InvariantCulture));
            }
        }
        return string.Join(" | ", parts);
    }

    private string GetGeneTypeLabel(DNA.Type type)
    {
        return type switch
        {
            DNA.Type.Inferior => T("低级", "Inferior"),
            DNA.Type.Superior => T("高级", "Superior"),
            DNA.Type.Brain => T("大脑", "Brain"),
            _ => T("普通", "Default")
        };
    }

    private void ExecuteSelectedPredation(
        Chara actor,
        Chara target,
        Act ability,
        Point targetPoint,
        bool partyTarget,
        Thing item)
    {
        if (!Enabled || actor == null || target == null || ability == null || item == null)
            return;
        _pending = new PendingSelection(actor, target, item);
        try
        {
            _executingSelectedPredation = true;
            if (!actor.UseAbility(ability, target, targetPoint, partyTarget))
                _pending = null;
        }
        catch
        {
            _pending = null;
            throw;
        }
        finally
        {
            _executingSelectedPredation = false;
        }
    }

    private void ReplacePredationFood(Chara actor, ref Thing food)
    {
        var pending = _pending;
        if (!Enabled || pending == null || !ReferenceEquals(actor, pending.Actor) ||
            !(actor.ai is AI_Fuck action) ||
            action.variation != AI_Fuck.Variation.Slime ||
            !ReferenceEquals(action.target, pending.Target))
            return;

        var selected = pending.Item;
        _pending = null;
        selected.MakeFoodFrom(pending.Target);
        selected.elements.ModBase(10, 20);
        selected.elements.ModBase(18, 100);
        food = selected;
    }

    private void ClearFinishedSelection(AI_Fuck action)
    {
        var pending = _pending;
        if (pending != null && action != null &&
            ReferenceEquals(action.owner, pending.Actor) &&
            ReferenceEquals(action.target, pending.Target) &&
            action.variation == AI_Fuck.Variation.Slime)
            _pending = null;
    }

    private string T(string zh, string en)
    {
        return _host.TranslateModuleText(zh, en);
    }

    private static bool CharaUseAbilityPrefix(
        Chara __instance,
        Act __0,
        Card __1,
        Point __2,
        bool __3,
        ref bool __result)
    {
        var module = _active;
        if (_executingSelectedPredation || module == null ||
            !module.TryHandleAbility(__instance, __0, __1, __2, __3))
            return true;
        __result = false;
        return false;
    }

    private static void FoodEffectProcPrefix(Chara __0, ref Thing __1)
    {
        _active?.ReplacePredationFood(__0, ref __1);
    }

    private static void AiFuckFinishPostfix(AI_Fuck __instance)
    {
        _active?.ClearFinishedSelection(__instance);
    }
}
