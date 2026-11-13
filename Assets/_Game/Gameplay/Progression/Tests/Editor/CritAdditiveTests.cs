using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Progression.Tests
{
    /// <summary>
    /// Build 17, item 7.1 (owner, 13 Nov 2026): Redline and Killer Instinct ADD to the crit rate (base + gear), never multiply it, and the
    /// total is capped by <see cref="CombatStatTuningData.maxCriticalRate"/>.
    /// </summary>
    public sealed class CritAdditiveTests
    {
        private const string Skills = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/";
        private const string TuningPath = "Assets/_Game/Gameplay/Progression/Data/Assets/CombatTuning/CombatStatTuning.asset";

        private static float CritFrom(SkillDefinitionSO skill, int rank)
        {
            float sum = 0f;
            foreach (StatModifierPayload p in skill.GetStatModifiers(rank))
            {
                if (p.statType != StatType.CriticalRate) continue;
                Assert.AreEqual(ModifierType.Flat, p.modifierType, skill.name + ": a crit buff adds, it never multiplies");
                sum += p.value;
            }
            return sum;
        }

        private static float WithBuff(CharacterCombatStats stats, SkillDefinitionSO skill, int rank)
        {
            var mods = new List<StatModifier>();
            foreach (StatModifierPayload p in skill.GetStatModifiers(rank)) mods.Add(new StatModifier(p.statType, p.modifierType, p.value, skill));
            stats.SetSourceModifiers(skill, mods);
            float crit = stats.GetStatValue(StatType.CriticalRate);
            stats.SetSourceModifiers(skill, new List<StatModifier>());
            return crit;
        }

        [Test]
        public void BasePlusGearPlusABuffIsASum()
        {
            var redline = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(Skills + "Skill_SpiritualRage.asset");
            var killer = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(Skills + "Skill_HeavenlyBlow.asset");
            var tuning = AssetDatabase.LoadAssetAtPath<CombatStatTuningData>(TuningPath);
            Assert.IsNotNull(redline); Assert.IsNotNull(killer); Assert.IsNotNull(tuning);

            var stats = new GameObject("CritAdditiveTests").AddComponent<CharacterCombatStats>();
            try
            {
                stats.TuningData = tuning;
                stats.SetSourceModifiers(stats, new List<StatModifier> { new StatModifier(StatType.CriticalRate, ModifierType.Flat, 20f, stats) }); // gear
                float gear = stats.GetStatValue(StatType.CriticalRate);
                Assert.AreEqual(tuning.baselineCriticalRate + 20f, gear, 1e-3, "base 15 + gear 20 = 35");

                foreach (SkillDefinitionSO skill in new[] { redline, killer })
                    foreach (int rank in new[] { skill.minRank, skill.maxRank })
                    {
                        float x = CritFrom(skill, rank);
                        Assert.Greater(x, 0f, skill.name);
                        Assert.AreEqual(Mathf.Min(gear + x, tuning.maxCriticalRate), WithBuff(stats, skill, rank), 1e-3, $"{skill.name} rank {rank}: 35 + X");
                    }
            }
            finally { Object.DestroyImmediate(stats.gameObject); }
        }

        [Test]
        public void TheCapHolds()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<CombatStatTuningData>(TuningPath);
            var stats = new GameObject("CritAdditiveTests.cap").AddComponent<CharacterCombatStats>();
            try
            {
                stats.TuningData = tuning;
                stats.SetSourceModifiers(stats, new List<StatModifier> { new StatModifier(StatType.CriticalRate, ModifierType.Flat, 500f, stats) });
                Assert.AreEqual(tuning.maxCriticalRate, stats.GetStatValue(StatType.CriticalRate), 1e-3);
            }
            finally { Object.DestroyImmediate(stats.gameObject); }
            Assert.AreEqual(40f, CharacterCombatStats.CapCritical(40f, 75f));
            Assert.AreEqual(75f, CharacterCombatStats.CapCritical(90f, 75f));
            Assert.AreEqual(100f, CharacterCombatStats.CapCritical(130f, 250f), "never over 100");
        }
    }
}
