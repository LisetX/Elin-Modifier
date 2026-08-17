using System.Collections.Generic;

internal sealed class NpcRecord
{
    internal string Id = "";
    internal string Name = "";
    internal string Race = "";
    internal string Job = "";
    internal string Biome = "";
    internal string Hostility = "";
    internal string Category = "";
    internal string Equipment = "";
    internal int BaseLevel;
    internal int Chance;
    internal int Quality;
    internal SourceChara.Row Row = null!;
}

internal sealed class LocationResult
{
    internal string Name = "";
    internal string Route = "";
    internal double PeakProbability;
    internal int PeakDangerLevel;
    internal int MinimumDangerLevel;
}

internal sealed class NpcLootEntry
{
    internal string Source = "";
    internal string Item = "";
    internal string Probability = "";
    internal string Quantity = "";
    internal string Conditions = "";
}

internal sealed class NpcTemplateValue
{
    internal int Id;
    internal int Sort;
    internal string Name = "";
    internal int Value;
    internal string TooltipText = "";
    internal bool IsWeight;
    internal bool IsResistance;
    internal bool HasRandomRange;
    internal int RandomMinimum;
    internal int RandomMaximum;
}

internal sealed class NpcTemplateRandomRange
{
    internal int Minimum;
    internal int Maximum;
}

internal sealed class NpcEquipmentEntry
{
    internal string Id = "";
    internal string Name = "";
    internal string SlotName = "";
    internal bool IsRanged;
    internal bool IsCarried;
    internal int Quantity = 1;
    internal Thing Item = null!;
}

internal sealed class NpcBodySlotEntry
{
    internal int ElementId;
    internal int Index;
    internal string Name = "";
    internal SourceElement.Row Element = null!;
}

internal sealed class NpcTemplateInfo
{
    internal bool Loaded;
    internal int Life;
    internal int Mana;
    internal int Vigor;
    internal int Speed;
    internal int DV;
    internal int PV;
    internal int WeightLimit;
    internal bool WeightLimitHasRandomRange;
    internal int WeightLimitRandomMinimum;
    internal int WeightLimitRandomMaximum;
    internal string Error = "";
    internal readonly Dictionary<int, NpcTemplateRandomRange> RandomRanges =
        new Dictionary<int, NpcTemplateRandomRange>();
    internal readonly List<NpcBodySlotEntry> BodySlots = new List<NpcBodySlotEntry>();
    internal readonly List<NpcEquipmentEntry> Equipment = new List<NpcEquipmentEntry>();
    internal readonly List<NpcTemplateValue> MainAbilities = new List<NpcTemplateValue>();
    internal readonly List<NpcTemplateValue> Skills = new List<NpcTemplateValue>();
    internal readonly List<NpcTemplateValue> Feats = new List<NpcTemplateValue>();
    internal readonly List<NpcTemplateValue> Spells = new List<NpcTemplateValue>();
    internal readonly List<NpcTemplateValue> Resistances = new List<NpcTemplateValue>();
    internal readonly List<NpcTemplateValue> Enchantments = new List<NpcTemplateValue>();
}

internal sealed class NpcAnalysis
{
    internal NpcRecord Npc = null!;
    internal readonly List<LocationResult> Locations = new List<LocationResult>();
    internal readonly List<string> SpawnLists = new List<string>();
    internal readonly List<NpcLootEntry> Loot = new List<NpcLootEntry>();
    internal string HighestLocation = "";
    internal int PeakDangerLevel;
    internal int MinimumDangerLevel;
    internal double PeakProbability;
    internal double CurrentZoneProbability;
    internal NpcTemplateInfo Template = new NpcTemplateInfo();
}

internal sealed class ZoneNpcResult
{
    internal NpcRecord Npc = null!;
    internal double Probability;
    internal string MainRoute = "";
}

internal sealed class ZoneAnalysis
{
    internal string ZoneName = "";
    internal string ZoneType = "";
    internal string CurrentBiome = "";
    internal string BiomeCoverage = "";
    internal string Scaling = "";
    internal int DangerLevel;
    internal int ExistingNpcCount;
    internal int ExistingHostileCount;
    internal bool IsEstimate;
    internal readonly List<ZoneNpcResult> Npcs = new List<ZoneNpcResult>();
}
