using System;
using System.Collections.Generic;
using BepInEx.Logging;

internal sealed class ElinModifierModuleRegistry : IDisposable
{
    private readonly ElinModifierModuleManager _manager;
    private readonly ElinModifierServiceProvider _services;
    private readonly IElinModifierGameServices _gameServices;

    internal ElinModifierModuleRegistry(ElinModifierPlugin host, ManualLogSource logger)
    {
        if (host == null)
            throw new ArgumentNullException(nameof(host));
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));

        _services = new ElinModifierServiceProvider();
        IElinModifierGameServices? initializedGameServices = null;
        ElinModifierModuleManager? createdManager = null;
        try
        {
            var binder = _services.Register<IGameMemberBinder>(new GameMemberBinder());
            _gameServices = _services.Register<IElinModifierGameServices>(
                new ElinModifierGameServices(binder));
            RegisterGameServiceInterfaces(_gameServices);
            GameAccess.Initialize(_gameServices);
            initializedGameServices = _gameServices;

            createdManager = new ElinModifierModuleManager(host, logger, _services);
            _manager = createdManager;

            CharacterProtection = new CharacterProtectionModule();
            Harmony = new HarmonyPatchModule();
            AiHttpTransport = new AiHttpTransportModule();
            InteractionReflection = new InteractionReflectionModule();
            LGuiFocus = new LGuiFocusModule();
            ConfigurationStorage = new ConfigurationStorageModule();
            CwlErrorNotifications = new CwlErrorNotificationModule();
            DebugSimulation = new DebugSimulationModule();
            MainMenuInfo = new MainMenuInfoModule();
            TermsConfirmation = new TermsConfirmationModule();
            Watermark = new WatermarkModule(host);
            ThreatOverlay = new ThreatOverlayModule(host);
            Optimization = new OptimizationModule(host);
            Probability = new ProbabilityModule(host);
            Automation = new AutomationModule(host);
            Moongate = new MoongateModule(host);
            NpcInfo = new NpcInfoModule(host);
            Progression = new ProgressionModule();
            PlantHarvestMultiplier = new PlantHarvestMultiplierModule();
            IgnoreCropGrowthConditions = new IgnoreCropGrowthConditionsModule();
            AllFeatsLearnable = new AllFeatsLearnableModule(
                host,
                _gameServices.Sources,
                _gameServices.Characters,
                binder);
            CharacterPanelGenes = new CharacterPanelGenesModule(binder);
            AllowPcGeneImplant = new AllowPcGeneImplantModule(
                _gameServices.Characters,
                binder);
            GuaranteedGatheringRewards = new GuaranteedGatheringRewardsModule();
            SpecialNpcHatch = new SpecialNpcHatchModule();
            SpecialNpcCapture = new SpecialNpcCaptureModule();
            FishingNoWait = new FishingNoWaitModule();
            GeneSynthesisNoWait = new GeneSynthesisNoWaitModule();
            SleepWithoutSleepiness = new SleepWithoutSleepinessModule();
            AllPurposeWorkbench = new AllPurposeWorkbenchModule();
            RightClickInterrupt = new RightClickInterruptModule();
            AiInstruction = new AiInstructionModule(host);
            MerchantRefreshNoCost = new MerchantRefreshNoCostModule(binder);
            OneClickQuestCompletion = new OneClickQuestCompletionModule(host, _gameServices.Runtime, binder);
            MerchantMonsterBall = new MerchantMonsterBallModule();
            Nightly = NightlyModule.TryCreate(binder);
            MoreInfo = new MoreInfoModule(host);
            ExceptionTrace = new ExceptionTraceModule(host);

            RegisterInfrastructure(host, logger);
            RegisterFeatures(host);
            RegisterRuntime(host, logger);
        }
        catch
        {
            var cleanupFailures = new List<Exception>();
            TryConstructorCleanup(
                cleanupFailures,
                () => createdManager?.Dispose());
            if (initializedGameServices != null)
            {
                TryConstructorCleanup(
                    cleanupFailures,
                    () => GameAccess.Reset(initializedGameServices));
            }
            TryConstructorCleanup(cleanupFailures, _services.Dispose);
            if (cleanupFailures.Count > 0)
            {
                try
                {
                    logger.LogError(
                        "Module registry constructor cleanup failed: " +
                        new AggregateException(cleanupFailures));
                }
                catch
                {
                }
            }
            throw;
        }
    }

    internal IReadOnlyList<IElinModifierModule> All => _manager.All;
    internal ElinModifierServiceProvider Services => _services;

    internal CharacterProtectionModule CharacterProtection { get; }
    internal HarmonyPatchModule Harmony { get; }
    internal AiHttpTransportModule AiHttpTransport { get; }
    internal InteractionReflectionModule InteractionReflection { get; }
    internal LGuiFocusModule LGuiFocus { get; }
    internal ConfigurationStorageModule ConfigurationStorage { get; }
    internal CwlErrorNotificationModule CwlErrorNotifications { get; }
    internal DebugSimulationModule DebugSimulation { get; }
    internal MainMenuInfoModule MainMenuInfo { get; }
    internal TermsConfirmationModule TermsConfirmation { get; }
    internal WatermarkModule Watermark { get; }
    internal ThreatOverlayModule ThreatOverlay { get; }
    internal OptimizationModule Optimization { get; }
    internal ProbabilityModule Probability { get; }
    internal AutomationModule Automation { get; }
    internal MoongateModule Moongate { get; }
    internal NpcInfoModule NpcInfo { get; }
    internal ProgressionModule Progression { get; }
    internal PlantHarvestMultiplierModule PlantHarvestMultiplier { get; }
    internal IgnoreCropGrowthConditionsModule IgnoreCropGrowthConditions { get; }
    internal AllFeatsLearnableModule AllFeatsLearnable { get; }
    internal CharacterPanelGenesModule CharacterPanelGenes { get; }
    internal AllowPcGeneImplantModule AllowPcGeneImplant { get; }
    internal GuaranteedGatheringRewardsModule GuaranteedGatheringRewards { get; }
    internal SpecialNpcHatchModule SpecialNpcHatch { get; }
    internal SpecialNpcCaptureModule SpecialNpcCapture { get; }
    internal FishingNoWaitModule FishingNoWait { get; }
    internal GeneSynthesisNoWaitModule GeneSynthesisNoWait { get; }
    internal SleepWithoutSleepinessModule SleepWithoutSleepiness { get; }
    internal AllPurposeWorkbenchModule AllPurposeWorkbench { get; }
    internal RightClickInterruptModule RightClickInterrupt { get; }
    internal AiInstructionModule AiInstruction { get; }
    internal MerchantRefreshNoCostModule MerchantRefreshNoCost { get; }
    internal OneClickQuestCompletionModule OneClickQuestCompletion { get; }
    internal MerchantMonsterBallModule MerchantMonsterBall { get; }
    internal NightlyModule? Nightly { get; }
    internal MoreInfoModule MoreInfo { get; }
    internal ExceptionTraceModule ExceptionTrace { get; }

    internal void InitializeAll() => _manager.InitializeAll();
    internal void TickAll() => _manager.TickAll();
    internal void LateTickAll() => _manager.LateTickAll();
    internal void DrawGuiAll() => _manager.DrawGuiAll();
    internal void ShutdownAll() => _manager.ShutdownAll();
    public void Dispose()
    {
        try
        {
            _manager.Dispose();
        }
        finally
        {
            try
            {
                GameAccess.Reset(_gameServices);
            }
            finally
            {
                _services.Dispose();
            }
        }
    }

    private void RegisterGameServiceInterfaces(IElinModifierGameServices gameServices)
    {
        _services.Register<IGameRuntimeContext>(gameServices.Runtime);
        _services.Register<IGameClockAccess>(gameServices.Clock);
        _services.Register<IGameSourceRepository>(gameServices.Sources);
        _services.Register<ICharacterGameAccess>(gameServices.Characters);
        _services.Register<IWorldGameAccess>(gameServices.World);
        _services.Register<IGameUiAccess>(gameServices.Ui);
        _services.Register<IGameRandomService>(gameServices.Random);
        _services.Register<IGameSpawnService>(gameServices.Spawn);
        _services.Register<IMilkBonusPreviewService>(gameServices.MilkBonusPreview);
        _services.Register<IGameMessageService>(gameServices.Messages);
    }

    private static void TryConstructorCleanup(
        List<Exception> failures,
        Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    private void RegisterInfrastructure(ElinModifierPlugin host, ManualLogSource logger)
    {
        Register("service.game-access", -1100, 0, _gameServices);
        Register("service.configuration-storage", -1000, 0, ConfigurationStorage);
        Register("service.interaction-reflection", -990, 170, InteractionReflection,
            shutdown: InteractionReflection.Clear);
        Register("service.lgui-focus", -980, 180, LGuiFocus,
            shutdown: LGuiFocus.Clear);
        Register("service.ai-http", -970, 190, AiHttpTransport,
            shutdown: AiHttpTransport.Dispose);
        Register("service.debug-simulation", -960, 0, DebugSimulation);
        Register("core.terms-confirmation", -940, 0, TermsConfirmation);

        Register("core.debug-capture", 100, 110, host,
            initialize: host.InitializeModuleDebugAuthorization,
            shutdown: host.RemoveModuleDebugCapture);
        Register("emp.workspace", 200, 0, host,
            initialize: host.InitializeModuleEmpWorkspace,
            dependencies: new[] { "core.debug-capture" });
        Register("core.configuration", 300, 0, host,
            initialize: host.LoadModuleConfiguration,
            dependencies: new[] { "emp.workspace", "service.configuration-storage" });
        Register("core.optimization", 400, 80, Optimization,
            initialize: host.InitializeModuleOptimization,
            tick: host.TickOptimization,
            shutdown: host.ShutdownModuleOptimization,
            dependencies: new[] { "core.configuration" });
        Register("emp.runtime", 500, 100, host,
            initialize: host.ApplyModuleEmpStates,
            shutdown: host.UnpatchModuleAiChanges,
            dependencies: new[] { "core.configuration", "core.optimization" });
        Register("core.harmony", 600, 160, Harmony,
            initialize: host.InstallModuleHarmonyPatches,
            shutdown: () => Harmony.Shutdown(logger),
            dependencies: new[] { "service.game-access", "core.configuration", "emp.runtime" });
        Register("service.cwl-error-notifications", 610, 150, CwlErrorNotifications,
            shutdown: CwlErrorNotifications.Shutdown,
            dependencies: new[] { "core.harmony" });

        if (Nightly != null)
        {
            Register("compatibility.nightly", 700, 140, Nightly,
                initialize: () => Nightly.Initialize(Harmony, logger),
                shutdown: Nightly.Shutdown,
                dependencies: new[] { "core.harmony" });
        }
    }

    private void RegisterFeatures(ElinModifierPlugin host)
    {
        Register("feature.character-protection", 1200, 0, CharacterProtection);
        Register("feature.progression", 1210, 0, Progression);
        Register("feature.plant-harvest-multiplier", 1220, 0, PlantHarvestMultiplier);
        Register("feature.ignore-crop-growth-conditions", 1230, 0, IgnoreCropGrowthConditions);
        Register("feature.all-feats-learnable", 1233, 0, AllFeatsLearnable);
        Register("feature.character-panel-genes", 1235, 0, CharacterPanelGenes);
        Register("feature.allow-pc-gene-implant", 1236, 0, AllowPcGeneImplant);
        Register("feature.guaranteed-gathering-rewards", 1240, 0, GuaranteedGatheringRewards);
        Register("feature.special-npc-hatch", 1250, 0, SpecialNpcHatch);
        Register("feature.special-npc-capture", 1260, 0, SpecialNpcCapture);
        Register("feature.fishing-no-wait", 1270, 0, FishingNoWait);
        Register("feature.gene-synthesis-no-wait", 1280, 0, GeneSynthesisNoWait);
        Register("feature.sleep-without-sleepiness", 1290, 0, SleepWithoutSleepiness);
        Register("feature.all-purpose-workbench", 1300, 0, AllPurposeWorkbench);
        Register("feature.right-click-interrupt", 200, 0, RightClickInterrupt,
            tick: RightClickInterrupt.Tick,
            dependencies: new[] { "core.configuration" });
        Register("feature.ai-instruction", 1305, 0, AiInstruction,
            tick: AiInstruction.Tick,
            lateTick: AiInstruction.LateTick,
            shutdown: AiInstruction.Shutdown);
        Register("feature.merchant-refresh-no-cost", 1310, 0, MerchantRefreshNoCost);
        Register("feature.one-click-quest-completion", 1315, 0, OneClickQuestCompletion,
            shutdown: OneClickQuestCompletion.Reset);
        Register("feature.merchant-monster-ball", 1320, 0, MerchantMonsterBall);
        Register("module.more-info", 1330, 0, MoreInfo);
        Register("module.exception-trace", 1340, 0, ExceptionTrace);
        Register("module.npc-compendium", 1350, 0, NpcInfo);
    }

    private void RegisterRuntime(ElinModifierPlugin host, ManualLogSource logger)
    {
        Register("ui.main-menu-info", 800, 40, MainMenuInfo,
            initialize: host.InitializeModuleMainMenuInfo,
            shutdown: host.ShutdownModuleMainMenuInfo,
            dependencies: new[] { "core.configuration" });
        Register("ui.lgui", 900, 70, host,
            initialize: host.InitializeLifecycleLGui,
            tick: host.TickModuleLGui,
            shutdown: host.ShutdownModuleLGui,
            dependencies: new[] { "core.configuration", "core.harmony" });
        Register("ui.watermark", 1000, 50, Watermark,
            initialize: host.InitializeModuleWatermark,
            tick: host.TickModuleWatermark,
            shutdown: host.ShutdownAndPersistModuleWatermark,
            dependencies: new[] { "core.configuration", "ui.lgui" });
        Register("ui.threat-overlay", 1100, 60, ThreatOverlay,
            initialize: host.InitializeModuleThreatOverlay,
            tick: host.TickModuleThreatOverlay,
            lateTick: host.LateTickModuleThreatOverlay,
            shutdown: host.ShutdownModuleThreatOverlay,
            dependencies: new[] { "core.configuration", "ui.lgui" });

        Register("core.input", 100, 0, host,
            tick: host.TickModuleInput,
            lateTick: host.LateTickModuleInput);
        Register("core.gameplay-maintenance", 400, 0, host,
            tick: host.TickModuleGameplayMaintenance,
            dependencies: new[] { "core.optimization", "core.harmony" });
        Register("module.automation", 600, 20, Automation,
            tick: Automation.TickAutomation,
            shutdown: Automation.Shutdown,
            dependencies: new[] { "core.configuration", "core.optimization" });
        Register("module.probability", 700, 10, Probability,
            tick: Probability.Tick,
            shutdown: host.RestoreModuleProbabilityValues,
            dependencies: new[] { "core.configuration", "core.harmony" });
        Register("module.moongate", 800, 30, Moongate,
            tick: Moongate.TickWorldState,
            shutdown: Moongate.Shutdown,
            dependencies: new[] { "core.configuration", "core.harmony" });
        Register("feature.kill-growth-save-context", 900, 0, host,
            tick: host.TickModuleKillGrowthSaveContext,
            dependencies: new[] { "core.configuration" });

        Register("cleanup.equipment-comparison", 2000, 0, host,
            shutdown: host.ShutdownModuleEquipmentComparison);
        Register("cleanup.frame-rate", 2010, 120, host,
            shutdown: host.RestoreModuleFrameRate);
        Register("cleanup.food-overlays", 2020, 130, host,
            shutdown: host.ClearModuleFoodRotOverlays);
        Register("cleanup.plugin-singleton", 2030, 200, host,
            shutdown: host.ClearModuleSingleton);
        Register("cleanup.plugin-skin", 2040, 210, host,
            shutdown: host.DestroyModuleSkin);

        _ = logger;
    }

    private void Register(
        string id,
        int order,
        int shutdownOrder,
        object instance,
        Action<ElinModifierModuleContext>? attach = null,
        Action? initialize = null,
        Action? tick = null,
        Action? lateTick = null,
        Action? drawGui = null,
        Action? shutdown = null,
        string[]? dependencies = null)
    {
        var capabilities = ElinModifierModuleCapabilities.None;
        if (initialize != null)
            capabilities |= ElinModifierModuleCapabilities.Initialize;
        if (tick != null)
            capabilities |= ElinModifierModuleCapabilities.Update;
        if (lateTick != null)
            capabilities |= ElinModifierModuleCapabilities.LateUpdate;
        if (drawGui != null)
            capabilities |= ElinModifierModuleCapabilities.Gui;
        if (shutdown != null)
            capabilities |= ElinModifierModuleCapabilities.Shutdown;

        var descriptor = new ElinModifierModuleDescriptor(
            id,
            order,
            shutdownOrder,
            capabilities,
            dependencies ?? Array.Empty<string>());
        _manager.Register(new DelegateElinModifierModule(
            descriptor,
            instance,
            attach: attach,
            initialize: initialize,
            tick: tick,
            lateTick: lateTick,
            drawGui: drawGui,
            shutdown: shutdown));
    }
}
