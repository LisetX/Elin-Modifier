using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal sealed class CoreRegressionTestResult
{
    internal int Passed { get; set; }
    internal int Failed { get; set; }
    internal List<string> Failures { get; } = new List<string>();
    internal bool Success => Failed == 0;
}

internal static class CoreRegressionTests
{
    internal static CoreRegressionTestResult Run()
    {
        var result = new CoreRegressionTestResult();
        Check(result, "exact property lookup",
            ConfigurationValueDocument.For("{\"value\":7,\"valueExtra\":9}").GetInt("value", -1) == 7);
        Check(result, "missing property fallback",
            ConfigurationValueDocument.For("{\"other\":1}").GetInt("value", 42) == 42);
        Check(result, "escaped string",
            ConfigurationValueDocument.For("{\"text\":\"line1\\nline2\"}").GetString("text", "") == "line1\nline2");
        Check(result, "compatible true token",
            ConfigurationValueDocument.For("{\"enabled\":\"on\"}").GetBool("enabled", false));
        Check(result, "compatible false token",
            !ConfigurationValueDocument.For("{\"enabled\":\"disabled\"}").GetBool("enabled", true));
        Check(result, "invariant float",
            Math.Abs(ConfigurationValueDocument.For("{\"value\":1.25}").GetFloat("value", 0f) - 1.25f) < 0.0001f);
        Check(result, "invalid json fallback",
            ConfigurationValueDocument.For("{invalid").GetString("text", "fallback") == "fallback");
        Check(result, "raw json boolean passthrough",
            ConfigurationValueDocument.For("{\"nightly\":true}").GetRawJson("nightly") == "true");
        Check(result, "raw json object passthrough",
            ConfigurationValueDocument.For("{\"nightly\":{\"enabled\":true}}").GetRawJson("nightly") ==
            "{\"enabled\":true}");
        Check(result, "dungeon generation default danger",
            DungeonGenerationPolicy.DefaultRequestedDanger == 1);
        Check(result, "dungeon generation custom danger preserved",
            DungeonGenerationPolicy.ResolveCreationDanger(36) == 36);
        Check(result, "dungeon generation zero uses vanilla danger",
            DungeonGenerationPolicy.ResolveCreationDanger(0) == DungeonGenerationPolicy.VanillaRandomDanger);
        Check(result, "dungeon generation negative uses vanilla danger",
            DungeonGenerationPolicy.ResolveCreationDanger(-10) == DungeonGenerationPolicy.VanillaRandomDanger);
        Check(result, "dungeon generation world region allowed",
            DungeonGenerationPolicy.CanGenerateAtCurrentArea(true, false, false, true, true, true, true));
        Check(result, "dungeon generation moongate region rejected",
            !DungeonGenerationPolicy.CanGenerateAtCurrentArea(true, true, true, false, false, false, false));
        Check(result, "dungeon generation ordinary instance rejected",
            !DungeonGenerationPolicy.CanGenerateAtCurrentArea(false, false, true, true, false, false, false));
        Check(result, "gathering hardness keeps ordinary material value",
            GatheringThresholdPolicy.CalculateRequiredHardness(20, 100, false) == 20);
        Check(result, "gathering hardness follows object hp scaling",
            GatheringThresholdPolicy.CalculateRequiredHardness(20, 50, false) == 10);
        Check(result, "gathering hardness applies hard material multiplier",
            GatheringThresholdPolicy.CalculateRequiredHardness(20, 50, true) == 30);
        Check(result, "gathering skill requirement cannot be negative",
            GatheringThresholdPolicy.NormalizeRequiredSkillLevel(-1) == 0);
        Check(result, "disabled guaranteed dismantle preserves original roll",
            Math.Abs(GuaranteedGatheringRewardsPolicy.ResolveDismantleRoll(0.75f, false) - 0.75f) < 0.0001f);
        Check(result, "enabled guaranteed dismantle forces successful roll",
            GuaranteedGatheringRewardsPolicy.ResolveDismantleRoll(0.75f, true) ==
            GuaranteedGatheringRewardsPolicy.GuaranteedDismantleRoll);
        Check(result, "full dismantle refund returns every proportional ingredient",
            GuaranteedGatheringRewardsPolicy.CalculateFullRefundCount(2, 3, 2) == 3);
        Check(result, "full dismantle refund rounds indivisible ingredient upward",
            GuaranteedGatheringRewardsPolicy.CalculateFullRefundCount(1, 1, 2) == 1);
        Check(result, "full dismantle refund ignores invalid ingredient counts",
            GuaranteedGatheringRewardsPolicy.CalculateFullRefundCount(0, 10, 1) == 0);
        Check(result, "full dismantle refund requires a valid unrestricted recipe",
            GuaranteedGatheringRewardsPolicy.ShouldUseFullRecipeRefund(true, true, true, true));
        Check(result, "full dismantle refund preserves original restrictions",
            !GuaranteedGatheringRewardsPolicy.ShouldUseFullRecipeRefund(true, true, true, false));
        Check(result, "matching dismantled recipe attempt forces original discovery roll",
            GuaranteedGatheringRewardsPolicy.IsMatchingDismantleRecipeAttempt(
                true, "chair", "chair", 100, 100));
        Check(result, "unrelated recipe attempt keeps original discovery roll",
            !GuaranteedGatheringRewardsPolicy.IsMatchingDismantleRecipeAttempt(
                true, "chair", "table", 100, 100));
        Check(result, "stale dismantled recipe attempt keeps original discovery roll",
            !GuaranteedGatheringRewardsPolicy.IsMatchingDismantleRecipeAttempt(
                true, "chair", "chair", 99, 100));
        Check(result, "factory recipe source is learnable",
            GuaranteedGatheringRewardsPolicy.IsLearnableRecipeSource(true, true, false));
        Check(result, "quick-craft recipe source is learnable",
            GuaranteedGatheringRewardsPolicy.IsLearnableRecipeSource(true, false, true));
        Check(result, "non-crafting recipe source is ignored",
            !GuaranteedGatheringRewardsPolicy.IsLearnableRecipeSource(true, false, false));
        Check(result, "missing recipe source is ignored",
            !GuaranteedGatheringRewardsPolicy.IsLearnableRecipeSource(false, true, true));
        Check(result, "moongate all regular containers transferred",
            MoongateContainerTransferPolicy.ShouldTransfer(true, "chest_boss"));
        Check(result, "moongate debug container excluded",
            !MoongateContainerTransferPolicy.ShouldTransfer(true, "DebugContainer"));
        Check(result, "moongate non-container excluded",
            !MoongateContainerTransferPolicy.ShouldTransfer(false, "chest_boss"));
        Check(result, "moongate restored containers unlocked",
            MoongateContainerTransferPolicy.RestoredLockLevel == 0);
        Check(result, "moongate container count limit",
            MoongateContainerLimits.MaxContainerCount == 40960);
        Check(result, "moongate per-container item limit",
            MoongateContainerLimits.MaxItemsPerContainer == 100000);
        Check(result, "moongate total item limit",
            MoongateContainerLimits.MaxTotalItemCount == 1000000);
        Check(result, "moongate container key length limit",
            MoongateContainerLimits.MaxContainerKeyLength == 20480);
        CheckNpcInfoProbabilityMath(result);
        CheckNpcTemplateValueMath(result);
        CheckModuleGraph(result);
        CheckGameMemberBinder(result);
        CheckServiceProvider(result);
        CheckAtomicStorage(result);
        return result;
    }

    private static void CheckGameMemberBinder(CoreRegressionTestResult result)
    {
        using var binder = new GameMemberBinder();

        var publicField = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PublicFieldBindingFixture),
            typeof(int),
            GameValueAccess.ReadWrite,
            "Value"));
        var publicFieldTarget = new PublicFieldBindingFixture();
        publicField.Set(publicFieldTarget, 12);
        Check(result, "game binding public field read write",
            publicField.Status == GameBindingStatus.PublicReflection &&
            publicField.Get(publicFieldTarget) == 12);
        var positiveResolutionCount = binder.ValueResolutionCount;
        var publicFieldAgain = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PublicFieldBindingFixture),
            typeof(int),
            GameValueAccess.ReadWrite,
            "Value"));
        Check(result, "game binding positive value result cached",
            ReferenceEquals(publicField, publicFieldAgain) &&
            binder.ValueResolutionCount == positiveResolutionCount);

        var publicProperty = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PublicPropertyBindingFixture),
            typeof(int),
            GameValueAccess.ReadWrite,
            "Value"));
        var publicPropertyTarget = new PublicPropertyBindingFixture();
        publicProperty.Set(publicPropertyTarget, 18);
        Check(result, "game binding public property delegate",
            publicProperty.Status == GameBindingStatus.PublicDelegate &&
            publicProperty.Get(publicPropertyTarget) == 18);

        var privateProperty = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PrivatePropertyBindingFixture),
            typeof(int),
            GameValueAccess.ReadWrite,
            "Value"));
        var privatePropertyTarget = new PrivatePropertyBindingFixture();
        privateProperty.Set(privatePropertyTarget, 27);
        Check(result, "game binding private property fallback",
            privateProperty.Status == GameBindingStatus.NonPublicReflection &&
            privateProperty.Get(privatePropertyTarget) == 27 &&
            privatePropertyTarget.ReadValue() == 27);

        var protectedField = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(ProtectedBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "ProtectedValue"));
        Check(result, "game binding protected base field fallback",
            protectedField.Status == GameBindingStatus.NonPublicReflection &&
            protectedField.Get(new ProtectedBindingFixture()) == 31);

        var priorityField = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PublicPriorityBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "PriorityValue"));
        Check(result, "game binding derived nonpublic field precedes public base field",
            priorityField.Status == GameBindingStatus.NonPublicReflection &&
            priorityField.Get(new PublicPriorityBindingFixture()) == 99);

        var staticProperty = binder.BindValue<int>(GameValueSpec.Static(
            typeof(StaticBindingFixture),
            typeof(int),
            GameValueAccess.ReadWrite,
            "Value"));
        StaticBindingFixture.Reset();
        staticProperty.Set(null, 44);
        Check(result, "game binding static private property",
            staticProperty.Status == GameBindingStatus.NonPublicReflection &&
            staticProperty.Get(null) == 44 && StaticBindingFixture.ReadValue() == 44);

        var candidateValue = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(CandidateBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "LegacyValue",
            "CurrentValue"));
        Check(result, "game binding primary candidate precedes public alias",
            candidateValue.Status == GameBindingStatus.NonPublicReflection &&
            candidateValue.Get(new CandidateBindingFixture()) == 53);

        var aliasValue = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(CandidateBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "MissingValue",
            "CurrentValue"));
        Check(result, "game binding fallback candidate member name",
            aliasValue.Status == GameBindingStatus.PublicReflection &&
            aliasValue.Get(new CandidateBindingFixture()) == 61);

        var exactValueType = binder.BindValue<long>(GameValueSpec.Instance(
            typeof(PublicFieldBindingFixture),
            typeof(long),
            GameValueAccess.Read,
            "Value"));
        Check(result, "game binding value type is exact", !exactValueType.IsBound);

        var overload = binder.BindMethod(GameMethodSpec.Instance(
            typeof(MethodBindingFixture),
            typeof(string),
            new[] { typeof(string) },
            "Echo"));
        Check(result, "game binding exact overload signature",
            overload.Status == GameBindingStatus.PublicDelegate &&
            Equals(overload.Invoke(new MethodBindingFixture(), "text"), "string:text"));

        var priorityMethod = binder.BindMethod(GameMethodSpec.Instance(
            typeof(MethodPriorityBindingFixture),
            typeof(int),
            new[] { typeof(int) },
            "Resolve"));
        Check(result, "game binding derived nonpublic method precedes public base method",
            priorityMethod.Status == GameBindingStatus.NonPublicReflection &&
            Equals(priorityMethod.Invoke(new MethodPriorityBindingFixture(), 4), 103));

        var privateMethod = binder.BindMethod(GameMethodSpec.Instance(
            typeof(ProtectedBindingFixture),
            typeof(int),
            new[] { typeof(int) },
            "AddProtected"));
        Check(result, "game binding protected base method fallback",
            privateMethod.Status == GameBindingStatus.NonPublicReflection &&
            Equals(privateMethod.Invoke(new ProtectedBindingFixture(), 4), 35));

        var staticMethod = binder.BindMethod(GameMethodSpec.Static(
            typeof(MethodBindingFixture),
            typeof(int),
            new[] { typeof(int), typeof(int) },
            "Add"));
        Check(result, "game binding static public method",
            Equals(staticMethod.Invoke(null, 8, 9), 17));

        var publicGenericMethod = binder.BindMethod(GameMethodSpec.InstanceGeneric(
            typeof(MethodBindingFixture),
            typeof(string),
            new[] { typeof(int) },
            new[] { typeof(int) },
            "DescribeGeneric"));
        Check(result, "game binding closes public generic method",
            publicGenericMethod.Status == GameBindingStatus.PublicDelegate &&
            publicGenericMethod.Method?.ContainsGenericParameters == false &&
            Equals(publicGenericMethod.Invoke(new MethodBindingFixture(), 12), "Int32:12"));

        var privateGenericMethod = binder.BindMethod(GameMethodSpec.InstanceGeneric(
            typeof(MethodBindingFixture),
            typeof(string),
            new[] { typeof(string) },
            new[] { typeof(string) },
            "DescribePrivateGeneric"));
        Check(result, "game binding closes private generic method",
            privateGenericMethod.Status == GameBindingStatus.NonPublicReflection &&
            privateGenericMethod.Method?.ContainsGenericParameters == false &&
            Equals(privateGenericMethod.Invoke(new MethodBindingFixture(), "value"), "String:value"));

        var invalidGenericMethod = binder.BindMethod(GameMethodSpec.InstanceGeneric(
            typeof(MethodBindingFixture),
            typeof(string),
            new[] { typeof(int) },
            new[] { typeof(int) },
            "DescribeReferenceGeneric"));
        Check(result, "game binding rejects unsatisfied generic constraints",
            !invalidGenericMethod.IsBound);

        var referenceMethod = binder.BindMethod(GameMethodSpec.Instance(
            typeof(MethodBindingFixture),
            typeof(int),
            new[] { typeof(int).MakeByRefType() },
            "Increment"));
        var referenceArguments = new object?[] { 10 };
        var referenceResult = referenceMethod.Invoke(new MethodBindingFixture(), referenceArguments);
        Check(result, "game binding public delegate failure uses reflection",
            referenceMethod.Status == GameBindingStatus.PublicReflection &&
            Equals(referenceResult, 11) && Equals(referenceArguments[0], 11));

        var throwingMethod = binder.BindMethod(GameMethodSpec.Instance(
            typeof(MethodBindingFixture),
            typeof(void),
            Type.EmptyTypes,
            "ThrowFromBinding"));
        Check(result, "game binding invocation exception unwrapped",
            ThrowsBindingFixtureException(() => throwingMethod.Invoke(new MethodBindingFixture())));

        var missingValueSpec = GameValueSpec.Instance(
            typeof(PublicFieldBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "MissingValue");
        var valueResolutionCount = binder.ValueResolutionCount;
        var missingValueFirst = binder.BindValue<int>(missingValueSpec);
        var missingValueSecond = binder.BindValue<int>(GameValueSpec.Instance(
            typeof(PublicFieldBindingFixture),
            typeof(int),
            GameValueAccess.Read,
            "MissingValue"));
        Check(result, "game binding negative value result cached",
            !missingValueFirst.IsBound &&
            ReferenceEquals(missingValueFirst, missingValueSecond) &&
            binder.ValueResolutionCount == valueResolutionCount + 1);

        var missingMethodSpec = GameMethodSpec.Instance(
            typeof(MethodBindingFixture),
            typeof(void),
            Type.EmptyTypes,
            "MissingMethod");
        var methodResolutionCount = binder.MethodResolutionCount;
        var missingMethodFirst = binder.BindMethod(missingMethodSpec);
        var missingMethodSecond = binder.BindMethod(GameMethodSpec.Instance(
            typeof(MethodBindingFixture),
            typeof(void),
            Type.EmptyTypes,
            "MissingMethod"));
        Check(result, "game binding negative method result cached",
            !missingMethodFirst.IsBound &&
            ReferenceEquals(missingMethodFirst, missingMethodSecond) &&
            binder.MethodResolutionCount == methodResolutionCount + 1);

        var resetBinder = new GameMemberBinder();
        var resetBindingFirst = resetBinder.BindValue<int>(missingValueSpec);
        resetBinder.Clear();
        var resetBindingSecond = resetBinder.BindValue<int>(missingValueSpec);
        Check(result, "game binding clear resets live binder cache",
            !ReferenceEquals(resetBindingFirst, resetBindingSecond) &&
            resetBinder.ValueResolutionCount == 1);
        resetBinder.Dispose();
        Check(result, "game binding disposed binder rejects value binding",
            ThrowsObjectDisposed(() => resetBinder.BindValue<int>(missingValueSpec)));
        Check(result, "game binding disposed binder rejects method binding",
            ThrowsObjectDisposed(() => resetBinder.BindMethod(missingMethodSpec)));
        Check(result, "game binding disposed binder rejects cache reset",
            ThrowsObjectDisposed(resetBinder.Clear));
    }

    private static void CheckServiceProvider(CoreRegressionTestResult result)
    {
        var disposalOrder = new List<string>();
        var provider = new ElinModifierServiceProvider();
        var shared = new SharedProviderDisposable(disposalOrder);
        var dependent = new DependentProviderDisposable(disposalOrder);

        provider.Register<IProviderFirstAlias>(shared);
        provider.Register(dependent);
        provider.Register<IProviderSecondAlias>(shared);

        Check(result, "service provider resolves registered interface",
            ReferenceEquals(provider.GetRequired<IProviderFirstAlias>(), shared));
        Check(result, "service provider resolves shared interface alias",
            provider.TryGet<IProviderSecondAlias>(out var alias) && ReferenceEquals(alias, shared));

        var duplicateRejected = false;
        try
        {
            provider.Register<IProviderFirstAlias>(new SharedProviderDisposable(disposalOrder));
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        Check(result, "service provider rejects duplicate service type", duplicateRejected);

        provider.Dispose();
        provider.Dispose();
        Check(result, "service provider disposes aliases once in dependency order",
            shared.DisposeCount == 1 &&
            dependent.DisposeCount == 1 &&
            disposalOrder.Count == 2 &&
            disposalOrder[0] == "dependent" &&
            disposalOrder[1] == "shared");

        var disposedAccessRejected = false;
        try
        {
            provider.GetRequired<IProviderFirstAlias>();
        }
        catch (ObjectDisposedException)
        {
            disposedAccessRejected = true;
        }
        Check(result, "service provider rejects access after dispose", disposedAccessRejected);
    }

    private static void CheckModuleGraph(CoreRegressionTestResult result)
    {
        var ordered = ElinModifierModuleGraph.Order(new[]
        {
            new ElinModifierModuleDescriptor(
                "dependent",
                0,
                0,
                ElinModifierModuleCapabilities.None,
                "dependency"),
            new ElinModifierModuleDescriptor(
                "dependency",
                100,
                0,
                ElinModifierModuleCapabilities.None),
            new ElinModifierModuleDescriptor(
                "independent",
                50,
                0,
                ElinModifierModuleCapabilities.None)
        });
        Check(result, "module dependency precedes dependent",
            FindModuleIndex(ordered, "dependency") < FindModuleIndex(ordered, "dependent"));
        Check(result, "module order is deterministic",
            FindModuleIndex(ordered, "independent") < FindModuleIndex(ordered, "dependency"));
        Check(result, "duplicate module id rejected", ThrowsInvalidOperation(() =>
            ElinModifierModuleGraph.Order(new[]
            {
                NewTestModuleDescriptor("duplicate"),
                NewTestModuleDescriptor("duplicate")
            })));
        Check(result, "missing module dependency rejected", ThrowsInvalidOperation(() =>
            ElinModifierModuleGraph.Order(new[]
            {
                new ElinModifierModuleDescriptor(
                    "dependent",
                    0,
                    0,
                    ElinModifierModuleCapabilities.None,
                    "missing")
            })));
        Check(result, "circular module dependency rejected", ThrowsInvalidOperation(() =>
            ElinModifierModuleGraph.Order(new[]
            {
                new ElinModifierModuleDescriptor(
                    "left",
                    0,
                    0,
                    ElinModifierModuleCapabilities.None,
                    "right"),
                new ElinModifierModuleDescriptor(
                    "right",
                    0,
                    0,
                    ElinModifierModuleCapabilities.None,
                    "left")
            })));
    }

    private static ElinModifierModuleDescriptor NewTestModuleDescriptor(string id)
    {
        return new ElinModifierModuleDescriptor(
            id,
            0,
            0,
            ElinModifierModuleCapabilities.None);
    }

    private static int FindModuleIndex(
        IReadOnlyList<ElinModifierModuleDescriptor> modules,
        string id)
    {
        for (var i = 0; i < modules.Count; i++)
        {
            if (string.Equals(modules[i].Id, id, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static bool ThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void CheckNpcInfoProbabilityMath(CoreRegressionTestResult result)
    {
        var offsets = NpcInfoProbabilityMath.GetDefaultSpawnLevelOffsets(20);
        var probabilityTotal = 0d;
        foreach (var pair in offsets)
            probabilityTotal += pair.Value;
        Check(result, "npc info spawn level distribution normalized", Math.Abs(probabilityTotal - 1d) < 0.000000001d);
        Check(result, "npc info spawn level distribution bounded",
            offsets.Count > 0 && offsets.ContainsKey(0) && !offsets.ContainsKey(20));
        Check(result, "npc info default spawn candidate accepted",
            NpcInfoProbabilityMath.IsDefaultSpawnCandidate(false, false, false));
        Check(result, "npc info invalid spawn candidates rejected",
            !NpcInfoProbabilityMath.IsDefaultSpawnCandidate(true, false, false) &&
            !NpcInfoProbabilityMath.IsDefaultSpawnCandidate(false, true, false) &&
            !NpcInfoProbabilityMath.IsDefaultSpawnCandidate(false, false, true));
        var chance = NpcInfoProbabilityMath.DecodeLootValue(250);
        Check(result, "npc info loot chance decoding",
            Math.Abs(chance.Probability - 0.25d) < 0.000000001d && chance.MinimumQuantity == 1 && chance.MaximumQuantity == 1);
        var quantity = NpcInfoProbabilityMath.DecodeLootValue(1750);
        Check(result, "npc info loot quantity decoding",
            quantity.Probability == 1d && quantity.MinimumQuantity == 1 && quantity.MaximumQuantity == 2 &&
            Math.Abs(quantity.ExpectedQuantity - 1.75d) < 0.000000001d);
    }

    private static void CheckNpcTemplateValueMath(CoreRegressionTestResult result)
    {
        var levelOne = NpcTemplateValueMath.GetCharaSourceBounds(10, 1, 100);
        Check(result, "npc template level one fixed value",
            levelOne.FixedValue == 10 && levelOne.Minimum == 10 && levelOne.Maximum == 10);
        var levelTen = NpcTemplateValueMath.GetCharaSourceBounds(10, 10, 100);
        Check(result, "npc template fixed level growth",
            levelTen.FixedValue == 19 && levelTen.Minimum == 19);
        Check(result, "npc template random level bounds", levelTen.Maximum == 26);
        var noRandomFactor = NpcTemplateValueMath.GetCharaSourceBounds(25, 100, 0);
        Check(result, "npc template non-random values stay fixed",
            noRandomFactor.FixedValue == 25 && noRandomFactor.Minimum == 25 && noRandomFactor.Maximum == 25);
    }

    private static void Check(CoreRegressionTestResult result, string name, bool success)
    {
        if (success)
        {
            result.Passed++;
            return;
        }

        result.Failed++;
        result.Failures.Add(name);
    }

    private static void CheckAtomicStorage(CoreRegressionTestResult result)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ElinModifier.CoreRegression." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var path = Path.Combine(directory, "config.json");
        try
        {
            var storage = new ConfigurationStorageModule();
            storage.WriteAllTextAtomic(path, "{\"value\":1}", Encoding.UTF8);
            storage.WriteAllTextAtomic(path, "{\"value\":2}", Encoding.UTF8);
            Check(result, "atomic config replace",
                File.ReadAllText(path, Encoding.UTF8).IndexOf("\"value\":2", StringComparison.Ordinal) >= 0);
            Check(result, "atomic temp cleanup", !File.Exists(path + ".tmp"));
        }
        catch (Exception ex)
        {
            result.Failed++;
            result.Failures.Add("atomic config replace: " + ex.GetType().Name);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static bool ThrowsBindingFixtureException(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (BindingFixtureException ex)
        {
            return string.Equals(ex.Message, "binding failure", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ThrowsObjectDisposed(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private sealed class PublicFieldBindingFixture
    {
        public int Value = 1;
    }

    private sealed class PublicPropertyBindingFixture
    {
        public int Value { get; set; }
    }

    private sealed class PrivatePropertyBindingFixture
    {
        private int Value { get; set; }

        internal int ReadValue() => Value;
    }

    private class ProtectedBindingBase
    {
        protected int ProtectedValue = 31;

        protected int AddProtected(int value) => ProtectedValue + value;
    }

    private sealed class ProtectedBindingFixture : ProtectedBindingBase
    {
        internal int ReadProtectedValue() => ProtectedValue;
    }

    private class PublicPriorityBindingBase
    {
        public int PriorityValue = 7;
    }

    private sealed class PublicPriorityBindingFixture : PublicPriorityBindingBase
    {
        private new int PriorityValue = 99;

        internal int ReadHiddenPriorityValue() => PriorityValue;
    }

    private class MethodPriorityBindingBase
    {
        public int Resolve(int value) => value + 7;
    }

    private class MethodPriorityBindingFixture : MethodPriorityBindingBase
    {
        protected new int Resolve(int value) => value + 99;
    }

    private static class StaticBindingFixture
    {
        private static int Value { get; set; }

        internal static void Reset() => Value = 0;
        internal static int ReadValue() => Value;
    }

    private sealed class CandidateBindingFixture
    {
        private int LegacyValue = 53;
        public int CurrentValue = 61;

        internal int ReadLegacyValue() => LegacyValue;
    }

    private sealed class MethodBindingFixture
    {
        public string Echo(int value) => "int:" + value.ToString(CultureInfo.InvariantCulture);
        public string Echo(string value) => "string:" + value;

        public static int Add(int left, int right) => left + right;

        public string DescribeGeneric<T>(T value)
        {
            return typeof(T).Name + ":" + value;
        }

        private string DescribePrivateGeneric<T>(T value)
        {
            return typeof(T).Name + ":" + value;
        }

        public string DescribeReferenceGeneric<T>(T value)
            where T : class
        {
            return typeof(T).Name + ":" + value;
        }

        public int Increment(ref int value)
        {
            value++;
            return value;
        }

        public void ThrowFromBinding()
        {
            throw new BindingFixtureException("binding failure");
        }
    }

    private sealed class BindingFixtureException : Exception
    {
        internal BindingFixtureException(string message)
            : base(message)
        {
        }
    }

    private interface IProviderFirstAlias
    {
    }

    private interface IProviderSecondAlias
    {
    }

    private sealed class SharedProviderDisposable :
        IProviderFirstAlias,
        IProviderSecondAlias,
        IDisposable
    {
        private readonly List<string> _disposalOrder;

        internal SharedProviderDisposable(List<string> disposalOrder)
        {
            _disposalOrder = disposalOrder;
        }

        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            _disposalOrder.Add("shared");
        }
    }

    private sealed class DependentProviderDisposable : IDisposable
    {
        private readonly List<string> _disposalOrder;

        internal DependentProviderDisposable(List<string> disposalOrder)
        {
            _disposalOrder = disposalOrder;
        }

        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            _disposalOrder.Add("dependent");
        }
    }
}
