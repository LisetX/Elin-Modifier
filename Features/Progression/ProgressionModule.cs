using System;
using System.Globalization;
using UnityEngine;

internal sealed class ProgressionModule
{
    internal bool ExperienceMultiplierEnabled { get; set; }
    internal bool ExperienceMultiplierIncludePcFaction { get; set; } = true;
    internal float CharacterLevelExperienceMultiplier { get; set; } = 1f;
    internal float SkillExperienceMultiplier { get; set; } = 1f;
    internal float MagicExperienceMultiplier { get; set; } = 1f;
    internal float FoodPotentialGainMultiplier { get; set; } = 1f;
    internal float TrainingPotentialGainMultiplier { get; set; } = 1f;
    internal string CharacterLevelExperienceMultiplierText { get; set; } = "1";
    internal string SkillExperienceMultiplierText { get; set; } = "1";
    internal string MagicExperienceMultiplierText { get; set; } = "1";
    internal string FoodPotentialGainMultiplierText { get; set; } = "1";
    internal string TrainingPotentialGainMultiplierText { get; set; } = "1";
    internal bool FoodRestoresSpEnabled { get; set; }
    internal int FoodRestoresSpPercent { get; set; } = 10;
    internal bool OptimizeMeleeHitChance { get; set; }
    internal bool OptimizeMeleeHitChanceIncludeParty { get; set; } = true;
    internal bool PcFactionTrainerAllSkills { get; set; }

    internal void Reset()
    {
        ExperienceMultiplierEnabled = false;
        ExperienceMultiplierIncludePcFaction = true;
        CharacterLevelExperienceMultiplier = 1f;
        SkillExperienceMultiplier = 1f;
        MagicExperienceMultiplier = 1f;
        FoodPotentialGainMultiplier = 1f;
        TrainingPotentialGainMultiplier = 1f;
        FoodRestoresSpEnabled = false;
        FoodRestoresSpPercent = 10;
        OptimizeMeleeHitChance = false;
        OptimizeMeleeHitChanceIncludeParty = true;
        PcFactionTrainerAllSkills = false;
        SyncTextFields();
    }

    internal bool TryApplyMultiplierTextFields()
    {
        if (!TryParseMultiplier(CharacterLevelExperienceMultiplierText, out var characterLevel) ||
            !TryParseMultiplier(SkillExperienceMultiplierText, out var skill) ||
            !TryParseMultiplier(MagicExperienceMultiplierText, out var magic) ||
            !TryParseMultiplier(FoodPotentialGainMultiplierText, out var foodPotential) ||
            !TryParseMultiplier(TrainingPotentialGainMultiplierText, out var trainingPotential))
            return false;

        CharacterLevelExperienceMultiplier = characterLevel;
        SkillExperienceMultiplier = skill;
        MagicExperienceMultiplier = magic;
        FoodPotentialGainMultiplier = foodPotential;
        TrainingPotentialGainMultiplier = trainingPotential;
        SyncTextFields();
        return true;
    }

    internal void SyncTextFields()
    {
        CharacterLevelExperienceMultiplierText =
            CharacterLevelExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        SkillExperienceMultiplierText =
            SkillExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        MagicExperienceMultiplierText =
            MagicExperienceMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        FoodPotentialGainMultiplierText =
            FoodPotentialGainMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
        TrainingPotentialGainMultiplierText =
            TrainingPotentialGainMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static bool TryParseMultiplier(string text, out float value)
    {
        if (!float.TryParse(
                (text ?? "").Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value) ||
            value < 0f)
        {
            value = 1f;
            return false;
        }

        value = Mathf.Clamp(value, 0f, 1000000f);
        return true;
    }

    internal static int ScalePositiveValue(int value, float multiplier)
    {
        if (value <= 0)
            return value;
        if (multiplier <= 0f)
            return 0;

        var scaled = Math.Round(value * (double)multiplier, MidpointRounding.AwayFromZero);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    internal bool IsExperienceTarget(Card card)
    {
        if (card is not Chara chara)
            return false;
        return chara.IsPC || ExperienceMultiplierIncludePcFaction && chara.IsPCFaction;
    }

    internal static bool IsMagicExperienceElement(Element element)
    {
        var source = element?.source;
        if (source == null)
            return false;

        var spellLike = source.isSpell ||
                        string.Equals(source.categorySub, "spell", StringComparison.OrdinalIgnoreCase) ||
                        TextHas(source.type, "spell") ||
                        TextHas(source.group, "spell") ||
                        TextHas(source.category, "spell") ||
                        TextHas(source.categorySub, "spell");
        var abilityLike = spellLike ||
                          source.abilityType is { Length: > 0 } ||
                          TextHas(source.type, "ability") ||
                          TextHas(source.group, "ability") ||
                          TextHas(source.category, "ability") ||
                          TextHas(source.categorySub, "ability") ||
                          TextHas(source.type, "act") ||
                          TextHas(source.group, "act") ||
                          TextHas(source.category, "act") ||
                          TextHas(source.categorySub, "act");
        if (!abilityLike || source.isTrait || source.isAttribute)
            return false;
        return !source.isSkill || spellLike;
    }

    private static bool TextHas(string text, string value) =>
        !string.IsNullOrEmpty(text) &&
        text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    internal bool ShouldForcePcFactionTrainerChoice(Chara trainer) =>
        PcFactionTrainerAllSkills &&
        trainer != null &&
        trainer.IsPCFaction &&
        trainer.trait is TraitTrainer;

    internal static bool CalculateOptimizedMeleeHit(AttackProcess attack)
    {
        var attacker = attack.CC;
        var target = attack.TC;
        if (attacker.HasCondition<ConAmbush>())
        {
            attack.crit = true;
            return true;
        }
        if (attack.critFury)
        {
            attack.crit = true;
            return true;
        }
        if (attacker.HasCondition<ConSevenSense>() &&
            (attacker.HasElement(1244) || attacker.HasElement(1246) ||
             attacker.HasElement(1247) || attacker.HasElement(1253)))
            return true;

        if (target != null)
        {
            if (target.HasCondition<ConDim>() && GameAccess.Random.Next(4) == 0)
            {
                attack.crit = true;
                return true;
            }
            if (target.IsDeadOrSleeping)
            {
                attack.crit = true;
                return true;
            }

            var advancedEvasion = target.Evalue(151);
            if (advancedEvasion != 0 && attack.toHit < advancedEvasion * 10L)
            {
                var evasionPressure = (float)(attack.evasion * 100L) /
                                      Mathf.Clamp(attack.toHit, 1f, attack.toHit);
                if (evasionPressure > 300f && GameAccess.Random.Next(advancedEvasion + 250) > 100 ||
                    evasionPressure > 200f && GameAccess.Random.Next(advancedEvasion + 250) > 150 ||
                    evasionPressure > 150f && GameAccess.Random.Next(advancedEvasion + 250) > 200)
                {
                    attack.evadePlus = true;
                    return false;
                }
            }

            var perfectEvasion = target.Evalue(57);
            if (perfectEvasion > 0)
            {
                if (perfectEvasion > GameAccess.Random.Next(100))
                {
                    attack.evadePlus = true;
                    return false;
                }
            }
            else if (perfectEvasion < 0 && -perfectEvasion > GameAccess.Random.Next(100))
            {
                return true;
            }
        }

        if (GameAccess.Random.Next(20) == 0)
            return true;
        if (GameAccess.Random.Next(20) == 0)
            return false;
        if (attack.toHit < 1)
            return false;
        if (attack.evasion < 1)
            return true;

        var hitScore = Math.Pow(Math.Max(1d, (double)attack.toHit), 2d);
        var evasionScore = Math.Pow(Math.Max(1d, (double)attack.evasion), 2d);
        var coreChance = hitScore / (hitScore + evasionScore * (101d / 260d));
        var threshold = Math.Max(
            0,
            Math.Min(
                1000000,
                (int)Math.Round(coreChance * 1000000d, MidpointRounding.AwayFromZero)));
        if (GameAccess.Random.Next(1000000) >= threshold)
            return false;

        if (GameAccess.Random.Next(5000) < attacker.Evalue(73) + 50)
        {
            attack.crit = true;
            return true;
        }
        if ((float)(attacker.Evalue(90) +
                    (attack.weapon != null ? attack.weapon.Evalue(90, ignoreGlobalElement: true) : 0)) +
            Mathf.Sqrt(attacker.Evalue(134)) > GameAccess.Random.Next(200))
        {
            attack.crit = true;
            return true;
        }
        if (attacker.Evalue(1420) > 0)
        {
            var missingHpPercent = Mathf.Min(100, 100 - attacker.hp * 100 / attacker.MaxHP);
            var criticalPower = missingHpPercent * (50 + attacker.Evalue(1420) * 50) / 100;
            if (criticalPower >= 50 &&
                criticalPower * criticalPower * criticalPower * criticalPower / 3 > GameAccess.Random.Next(100000000))
            {
                attack.crit = true;
                return true;
            }
        }
        return true;
    }

}
