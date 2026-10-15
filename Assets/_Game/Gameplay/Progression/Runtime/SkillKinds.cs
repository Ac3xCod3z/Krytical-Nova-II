using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>What kind of skill one is, as the Arsenal groups them and as a Dossier or a hover names it.</summary>
    public enum SkillKind : byte
    {
        /// <summary>An attack that hits every enemy in a radius.</summary>
        Area = 0,
        /// <summary>An attack that lands once on one enemy.</summary>
        SingleHit = 1,
        /// <summary>An attack that lands twice.</summary>
        TwoHit = 2,
        /// <summary>An attack that lands three times or more.</summary>
        ThreeHit = 3,
        /// <summary>Anything that is not an attack: a buff, a shield, a stance, a channel.</summary>
        Ward = 4,
    }

    /// <summary>
    /// The kinds of combat skill, worked out from a skill's own numbers (no skill carries a kind of its own), and the
    /// one line that says what a skill does at a rank. Shared by the Arsenal (the Skills window), the Dossier's and
    /// Skill Chip's item card, and the Hotbar's hover, so a skill reads the same everywhere (owner, 15 Oct 2026).
    /// </summary>
    public static class SkillKinds
    {
        /// <summary>The names of the Arsenal's groups of attacks, in the order of <see cref="SkillKind"/>.</summary>
        public static readonly string[] AttackLabels = { "AREA", "SINGLE HIT", "TWO-HIT", "THREE-HIT" };

        public static SkillKind Of(SkillDefinitionSO skill)
        {
            if (skill == null || !skill.IsAttack) return SkillKind.Ward;
            if (skill.IsAreaAttack) return SkillKind.Area;
            if (skill.hitCount >= 3) return SkillKind.ThreeHit;
            return skill.hitCount == 2 ? SkillKind.TwoHit : SkillKind.SingleHit;
        }

        /// <summary>"AREA", "SINGLE HIT", "TWO-HIT", "THREE-HIT" or "WARD".</summary>
        public static string Label(SkillKind kind) => kind == SkillKind.Ward ? "WARD" : AttackLabels[(int)kind];

        public static string Label(SkillDefinitionSO skill) => Label(Of(skill));

        /// <summary>
        /// The kind in a sentence for an item card: "Area attack: 3 hits on every enemy within 6 m" or
        /// "Ward: a buff, 60 s".
        /// </summary>
        public static string KindLine(SkillDefinitionSO skill, int rank)
        {
            if (skill == null) return string.Empty;
            SkillKind kind = Of(skill);
            int hits = Mathf.Max(1, skill.hitCount);
            string strike = hits == 1 ? "one hit" : $"{hits} hits";
            switch (kind)
            {
                case SkillKind.Area: return $"Area attack: {strike} on every enemy within {skill.GetRange(rank):0.#} m";
                case SkillKind.SingleHit: return $"Single-hit attack: one hit on one enemy, {skill.GetRange(rank):0.#} m reach";
                case SkillKind.TwoHit: return $"Two-hit attack: {strike} on one enemy, {skill.GetRange(rank):0.#} m reach";
                case SkillKind.ThreeHit: return $"Three-hit attack: {strike} on one enemy, {skill.GetRange(rank):0.#} m reach";
                default:
                    float duration = skill.GetDuration(rank);
                    string what = skill.isAbsorptionShield ? "a shield" : skill.executionType == SkillExecutionType.Channeled ? "a channel"
                        : skill.category == SkillCategory.WeaponStance ? "a stance" : "a buff";
                    return duration > 0f ? $"Ward: {what}, {duration:0} s" : $"Ward: {what}";
            }
        }

        /// <summary>What the skill does at this rank, in one line: its damage, reach, what it gives, and its Flux cost.</summary>
        public static string Describe(SkillDefinitionSO skill, int rank)
        {
            if (skill == null) return string.Empty;
            float chi = skill.GetChiCost(rank);
            float dur = skill.GetDuration(rank);
            float progress = skill.GetRankProgress(rank);

            if (skill.IsAttack)
            {
                float damage = Mathf.Lerp(skill.minDamagePercent, skill.maxDamagePercent, progress);
                float bonus = Mathf.Lerp(skill.minBonusDamagePercent, skill.maxBonusDamagePercent, progress);
                string hits = skill.hitCount > 1 ? $" over {skill.hitCount} hits" : string.Empty;
                string yang = bonus > 100f ? $" · Yang {bonus:0.#}%" : string.Empty;
                string reach = skill.IsAreaAttack ? $"{skill.GetRange(rank):0.#} m radius (all enemies)" : $"{skill.GetRange(rank):0.#} m reach";
                return $"{damage:0}% damage{hits}{yang} · {reach} · {chi:0} Flux";
            }
            if (skill.rankScaledModifiers != null && skill.rankScaledModifiers.Length > 0)
            {
                var parts = new List<string>();
                foreach (var mod in skill.rankScaledModifiers)
                    parts.Add($"+{Mathf.Lerp(mod.minValue, mod.maxValue, progress):0.#}% {DescribeStat(mod.statType)}");
                string scope = skill.consumedOnNextHit ? $"next hit, within {dur:0}s" : $"{dur:0}s duration";
                return $"{string.Join(" · ", parts)} · {scope} · {chi:0} Flux";
            }

            if (skill.isAbsorptionShield)
            {
                float shieldPct = Mathf.Round(skill.GetShieldMultiplier(rank) * 100f);
                return $"{shieldPct}% Max HP Shield · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.hasDeadlyBlowBoost)
            {
                float deadly = skill.GetDeadlyBlowBonus(rank);
                return $"+{deadly:0.#}% Critical (mult) · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.hasLuckBoost)
            {
                float luck = skill.GetLuckBonus(rank);
                return $"+{luck:0.#}% Luck (mult) · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.hasReflection)
            {
                float refChance = skill.GetReflectionChance(rank);
                return $"{refChance:0.#}% Reflect Physical Dmg · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.hasShieldShatter)
            {
                float shatChance = skill.GetDestructionChance(rank);
                return $"{shatChance:0.#}% Shield Shatter & Dispel · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.hasAcupressureResistance)
            {
                float stunRes = skill.GetAcupressureResistance(rank);
                return $"+{stunRes:0.#}% Lockup Resist · {dur:0}s duration · {chi:0} Flux";
            }
            if (skill.executionType == SkillExecutionType.Channeled)
            {
                float health = skill.GetChannelHealthRecovery(rank);
                string restores = (health > 0f ? $"+{health:0.#}% HP · " : string.Empty) + $"+{skill.GetChannelChiRecovery(rank):0.#}% Flux";
                return $"{restores} every {skill.channelTickInterval:0.#}s · {dur:0}s channel · {chi:0} Flux";
            }
            if (skill.statModifiers != null && skill.statModifiers.Length > 0)
            {
                var mod = skill.statModifiers[0];
                return $"+{mod.value:0.#}% {mod.statType} · {dur:0}s duration · {chi:0} Flux";
            }
            return $"{dur:0}s duration · {chi:0} Flux";
        }

        public static string DescribeStat(StatType stat)
        {
            switch (stat)
            {
                case StatType.AttackPower: return "Damage";
                case StatType.CriticalRate: return "Critical Rate";
                case StatType.ElementalDefense: return "Elemental Defense";
                case StatType.ElementalAttack: return "Elemental Attack";
                case StatType.DefensePower: return "Defense";
                default: return StatDisplay.Name(stat);
            }
        }
    }
}
