using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private enum LGuiRoundedImageStyle
    {
        Standard,
        Capsule
    }
    private sealed class LGuiRoundedImageTarget : MonoBehaviour
    {
        public Sprite? OriginalSprite;
        public Image.Type OriginalType;
        public bool OriginalFillCenter;
        public bool Captured;
        public LGuiRoundedImageStyle Style;

        public void Capture(Image image)
        {
            if (Captured || image == null)
                return;
            OriginalSprite = image.sprite;
            OriginalType = image.type;
            OriginalFillCenter = image.fillCenter;
            Captured = true;
        }
    }
    private enum LGuiPage
    {
        Features,
        Character,
        Items,
        Npcs,
        PlayerInfo,
        Home,
        Probability,
        Automation,
        Nightly,
        Moongate,
        NpcInfo,
        Ai,
        Debug,
        Emp,
        Settings
    }
    private enum LGuiFeatureId
    {
        SimulateAdvance,
        GenerateDungeon,
        LowPerformance,
        UnlockFrameRate,
        InvincibleMode,
        IgnoreBuffEffects,
        HostileThreatMarker,
        ShowNpcMoreInfo,
        ShowItemMoreInfo,
        ShowBuffSpecificValues,
        ShowItemPanelEnchantLevels,
        ShowItemPanelItemValue,
        ShowMainAbilityExperience,
        EquipmentComparison,
        IgnoreFriendlyFire,
        WorkbenchIngredientReadingOptimization,
        ExperienceMultiplier,
        PlantHarvestMultiplier,
        IgnoreCropGrowthConditions,
        FoodRestoresSp,
        DismantleAlwaysReturnsMaterials,
        DismantlingAlwaysLearnsRecipe,
        OptimizeMeleeHitChance,
        PcFactionTrainerAllSkills,
        UnlimitedHomeResidentCap,
        UnlimitedPartyMemberCap,
        UnlimitedOfferingFaithPoints,
        IgnoreGodArtifactFaithRequirement,
        ShrineEffectSelection,
        InfiniteChargeAndAmmo,
        RodStacking,
        RightClickInterruptOperation,
        StealHandNoTargetLimit,
        StealHandUndetectable,
        MerchantRefreshNoCost,
        MerchantAlwaysStocksMonsterBall,
        MerchantMonsterBallLevelOptimization,
        IgnoreSpecialNpcHatchRestriction,
        IgnoreSpecialNpcCaptureRestriction,
        AffinityOnlyIncrease,
        KarmaOnlyIncrease,
        AttackCannotBeInterrupted,
        FishingNoWait,
        GeneSynthesisNoWait,
        SleepWithoutSleepiness,
        AllPurposeWorkbench,
        InfiniteSight,
        ShowFoodRot,
        IgnoreFoodDecay,
        NoCraftMaterials,
        UnlockCraftMaterials,
        UnlockCraftRecipes,
        CustomItemAmount,
        CustomItemData,
        CustomFoodData,
        CustomWeaponData,
        CustomGeneData,
        StethoscopeNoLimit,
        IgnoreTerrain,
        OptimizeVoid,
        NoTalkInterestLoss,
        KillGrowth
    }
    private enum LGuiCharacterAction
    {
        None,
        ReadOnly,
        NpcAffinity,
        NpcRelationshipOption,
        NpcPartyAction,
        NpcFaith,
        NpcRelationshipChoices,
        NpcPartyChoices,
        NpcFaithChoices,
        FaithSelect,
        FaithPiety,
        NpcGeneSelect,
        NpcGeneApply,
        NpcGeneAdd,
        NpcGeneField,
        NpcGeneTypePopup,
        NpcGeneEffectId,
        NpcGeneEffectValue,
        NpcGeneEffectAdd,
        NpcGeneEffectTablePopup,
        EtherSelect,
        EtherApply,
        EtherAdd,
        EtherField,
        EtherTablePopup
    }
    private sealed class LGuiFeatureRow
    {
        public readonly LGuiFeatureId Id;
        public readonly string Label;

        public LGuiFeatureRow(LGuiFeatureId id, string label)
        {
            Id = id;
            Label = label;
        }
    }
    private sealed class LGuiCharacterRow
    {
        public readonly string Header;
        public readonly RowDef? Row;
        public readonly AbilityDef? Ability;
        public readonly Chara? Target;
        public readonly bool IsPc;
        public readonly bool IsPotential;
        public readonly string SectionKey;
        public readonly bool Expanded;
        public readonly bool SupportsFilter;
        public readonly LGuiCharacterAction Action;
        public readonly int ActionIndex;
        public readonly string ActionPayload;
        public readonly string ActionSummary;
        public readonly string InputKey;
        public readonly int Depth;

        public bool IsHeader => Row == null && Ability == null && Action == LGuiCharacterAction.None;

        public LGuiCharacterRow(string header)
        {
            Header = header;
            Ability = null;
            SectionKey = "";
            Expanded = true;
            SupportsFilter = false;
            Action = LGuiCharacterAction.None;
            ActionIndex = -1;
            ActionPayload = "";
            ActionSummary = "";
            InputKey = "";
            Depth = 0;
        }

        public LGuiCharacterRow(string header, string sectionKey, bool expanded, bool supportsFilter = true, int depth = 0)
        {
            Header = header;
            Ability = null;
            SectionKey = sectionKey;
            Expanded = expanded;
            SupportsFilter = supportsFilter;
            Action = LGuiCharacterAction.None;
            ActionIndex = -1;
            ActionPayload = "";
            ActionSummary = "";
            InputKey = "";
            Depth = Math.Max(0, depth);
        }

        public LGuiCharacterRow(RowDef row, Chara target, bool isPc, bool isPotential, int depth = 1)
        {
            Header = "";
            Ability = null;
            Row = row;
            Target = target;
            IsPc = isPc;
            IsPotential = isPotential;
            SectionKey = "";
            Expanded = true;
            SupportsFilter = false;
            Action = LGuiCharacterAction.None;
            ActionIndex = -1;
            ActionPayload = "";
            ActionSummary = "";
            InputKey = "";
            Depth = Math.Max(0, depth);
        }

        public LGuiCharacterRow(AbilityDef ability, Chara target, bool isPc, int depth = 1)
        {
            Header = "";
            Ability = ability;
            Target = target;
            IsPc = isPc;
            IsPotential = false;
            SectionKey = "";
            Expanded = true;
            SupportsFilter = false;
            Action = LGuiCharacterAction.None;
            ActionIndex = -1;
            ActionPayload = "";
            ActionSummary = "";
            InputKey = "";
            Depth = Math.Max(0, depth);
        }

        public LGuiCharacterRow(string label, Chara target, bool isPc, LGuiCharacterAction action, int actionIndex = -1, string inputKey = "", string actionPayload = "", string actionSummary = "", int depth = 1)
        {
            Header = label;
            Ability = null;
            Target = target;
            IsPc = isPc;
            IsPotential = false;
            SectionKey = "";
            Expanded = true;
            SupportsFilter = false;
            Action = action;
            ActionIndex = actionIndex;
            ActionPayload = actionPayload ?? "";
            ActionSummary = actionSummary ?? "";
            InputKey = inputKey ?? "";
            Depth = Math.Max(0, depth);
        }
    }
    private sealed class LGuiEmpRow
    {
        public readonly EmpPluginDefinition Plugin;
        public readonly EmpFunctionDefinition Function;
        public readonly EmpFunctionState State;

        public LGuiEmpRow(EmpPluginDefinition plugin, EmpFunctionDefinition function, EmpFunctionState state)
        {
            Plugin = plugin;
            Function = function;
            State = state;
        }
    }
    private sealed class LGuiHomeRow
    {
        public readonly string Label;
        public readonly string InputKey;
        public readonly Func<string>? Current;
        public readonly Action<int>? Apply;
        public readonly Func<bool>? IsActive;
        public readonly Action<bool>? SetActive;
        public readonly string SectionKey;
        public readonly bool Expanded;
        public readonly int Depth;

        public bool IsHeader => Current == null || Apply == null;

        public LGuiHomeRow(string label)
        {
            Label = label;
            InputKey = "";
            SectionKey = "";
            Expanded = false;
            Depth = 0;
        }

        public LGuiHomeRow(string label, string sectionKey, bool expanded, int depth = 0)
        {
            Label = label;
            InputKey = "";
            SectionKey = sectionKey;
            Expanded = expanded;
            Depth = Math.Max(0, depth);
        }

        public LGuiHomeRow(string label, string inputKey, Func<string> current, Action<int> apply, Func<bool>? isActive = null, Action<bool>? setActive = null, int depth = 1)
        {
            Label = label;
            InputKey = inputKey;
            Current = current;
            Apply = apply;
            IsActive = isActive;
            SetActive = setActive;
            SectionKey = "";
            Expanded = true;
            Depth = Math.Max(0, depth);
        }
    }
    private sealed class LGuiDebugRoot
    {
        public readonly string Label;
        public readonly object Target;

        public LGuiDebugRoot(string label, object target)
        {
            Label = label;
            Target = target;
        }
    }
    private sealed class LGuiDebugRow
    {
        public readonly string Key;
        public readonly object Instance;
        public readonly DebugMember Member;
        public object? Value;
        public string Error;
        public Type ValueType;

        public LGuiDebugRow(string key, object instance, DebugMember member, object? value, string error)
        {
            Key = key;
            Instance = instance;
            Member = member;
            Value = value;
            Error = error ?? "";
            ValueType = value == null ? member.ValueType : value.GetType();
        }
    }
    private GameObject? _lGuiRoot;
    private GameObject? _lGuiOwnedEventSystem;
    private Canvas? _lGuiCanvas;
    private CanvasScaler? _lGuiCanvasScaler;
    private CanvasGroup? _lGuiRootGroup;
    private LGuiFadeDriver? _lGuiRootFade;
    private CanvasGroup? _lGuiWindowGroup;
    private LGuiFadeDriver? _lGuiWindowFade;
    private Image? _lGuiBlockerImage;
    private Image? _lGuiWindowImage;
    private Mask? _lGuiWindowMask;
    private Image? _lGuiHeaderImage;
    private Image? _lGuiSidebarImage;
    private RectTransform? _lGuiWindow;
    private RectTransform? _lGuiPageHost;
    private Text? _lGuiTitle;
    private Text? _lGuiCredit;
    private Text? _lGuiVersion;
    private RectTransform? _lGuiGlobalConfigSaveRect;
    private RectTransform? _lGuiGlobalConfigLoadRect;
    private Text? _lGuiGlobalConfigSaveLabel;
    private Text? _lGuiGlobalConfigLoadLabel;
    private Text? _lGuiStatus;
    private Text? _lGuiHomeSelectionText;
    private Text? _lGuiCharacterTargetText;
    private Text? _lGuiPlayerInfoStatusText;
    private Text? _lGuiAiStatusText;
    private Text? _lGuiEmpStatusText;
    private LGuiScrollableTextBox? _lGuiAiResponseInput;
    private InputField? _lGuiAiPromptInput;
    private LGuiScrollableTextBox? _lGuiAiLastRequestInput;
    private LGuiScrollableTextBox? _lGuiAiLastResponseInput;
    private InputField? _lGuiAiModelInput;
    private InputField? _lGuiNpcIdInput;
    private readonly List<Text> _lGuiNpcRelationshipLabels = new List<Text>();
    private Font? _lGuiFont;
    private float _lGuiNextFontRefreshAt;
    private Texture2D? _lGuiRoundedWindowTexture;
    private Sprite? _lGuiRoundedWindowSprite;
    private Texture2D? _lGuiRoundedCapsuleTexture;
    private Sprite? _lGuiRoundedCapsuleSprite;
    private bool _lGuiInitialized;
    private bool _lGuiVisible;
    private bool _lGuiDataDirty = true;
    private LGuiPage _lGuiPage;
    private VirtualList<LGuiFeatureRow>? _lGuiFeatureList;
    private VirtualList<LGuiCharacterRow>? _lGuiCharacterList;
    private VirtualList<ItemDef>? _lGuiItemList;
    private VirtualList<NpcDef>? _lGuiNpcList;
    private VirtualList<LGuiHomeRow>? _lGuiHomeList;
    private VirtualList<LGuiDebugRow>? _lGuiDebugList;
    private VirtualList<LGuiEmpRow>? _lGuiEmpList;
    private readonly List<LGuiFeatureRow> _lGuiFeatureRows = new List<LGuiFeatureRow>();
    private readonly List<LGuiCharacterRow> _lGuiCharacterRows = new List<LGuiCharacterRow>();
    private readonly List<ItemDef> _lGuiFilteredItems = new List<ItemDef>();
    private readonly List<NpcDef> _lGuiFilteredNpcs = new List<NpcDef>();
    private readonly List<LGuiHomeRow> _lGuiHomeRows = new List<LGuiHomeRow>();
    private readonly List<LGuiDebugRoot> _lGuiDebugRoots = new List<LGuiDebugRoot>();
    private readonly List<LGuiDebugRow> _lGuiDebugRows = new List<LGuiDebugRow>();
    private readonly List<object> _lGuiDebugObjectStack = new List<object>();
    private readonly List<string> _lGuiDebugPathStack = new List<string>();
    private readonly List<LGuiEmpRow> _lGuiEmpRows = new List<LGuiEmpRow>();
    private readonly Dictionary<string, string> _lGuiCharacterSectionFilters = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _lGuiCharacterSectionExpanded = new Dictionary<string, bool>(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _lGuiHomeSectionExpanded = new Dictionary<string, bool>(StringComparer.Ordinal);
    private readonly Dictionary<LGuiPage, Button> _lGuiNavButtons = new Dictionary<LGuiPage, Button>();
    private readonly Dictionary<LGuiPage, Text> _lGuiNavLabels = new Dictionary<LGuiPage, Text>();
    private string _lGuiHomeFilter = "";
    private string _lGuiDebugFilter = "";
    private int _lGuiDebugRootIndex;
    private object? _lGuiDebugTarget;
    private string _lGuiDebugTargetLabel = "";
    private string _lGuiDebugTargetPath = "";
    private Text? _lGuiDebugTargetText;
    private GameObject? _lGuiEditorModal;
    private bool _lGuiModalHidesMain;
    private bool _lGuiModalRestoreMainOnClose;
    private int _lGuiCharacterTargetUid = -1;
    private int _lGuiFaithSelectionTargetUid = int.MinValue;
    private int _lGuiFaithSelectionIndex = -1;
    private const float LGuiMainFadeInSeconds = 0.18f;
    private const float LGuiMainFadeOutSeconds = 0.15f;
    private const float LGuiModalFadeInSeconds = 0.16f;
    private const float LGuiModalFadeOutSeconds = 0.13f;
}
