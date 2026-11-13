using System.Collections.Generic;
using KryticalNova.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Progression.Tests
{
    /// <summary>
    /// Build 17, items 9.1 and 9.3 (owner, 13 Nov 2026): Aegis gives +50 % Defense at its top rank; every class's Guard ward gives +10 %
    /// Elemental Attack at its top rank beside its Elemental Defense, so the elemental part of a hit is 10 % higher (against no ward).
    /// </summary>
    public sealed class AegisAndGuardTests
    {
        private const string Skills = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/";
        private const string AegisPath = Skills + "SelfBuffSkills/Skill_HeavenlyShield.asset";

        private static readonly string[] GuardPaths =
        {
            Skills + "BladeSkills/Skill_AzureWall.asset",
            Skills + "ArcCasterSkills/Skill_ArcCaster_Guard.asset",
            Skills + "CarbineSkills/Skill_Carbine_Guard.asset",
            Skills + "ClaymoreSkills/Skill_Claymore_Guard.asset",
            Skills + "GlaiveSkills/Skill_Glaive_Guard.asset",
            Skills + "HarvesterSkills/Skill_Harvester_Guard.asset",
            Skills + "LongaxeSkills/Skill_Longaxe_Guard.asset",
            Skills + "ServoClawsSkills/Skill_ServoClaws_Guard.asset",
            Skills + "WarbowSkills/Skill_Warbow_Guard.asset",
        };

        private static float Percent(SkillDefinitionSO skill, int rank, StatType stat)
        {
            float sum = 0f;
            foreach (StatModifierPayload p in skill.GetStatModifiers(rank))
                if (p.statType == stat && p.modifierType == ModifierType.PercentAdd) sum += p.value;
            return sum;
        }

        [Test]
        public void AegisGivesFiftyPercentDefenseAtItsTopRank()
        {
            var aegis = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AegisPath);
            Assert.IsNotNull(aegis, AegisPath);
            Assert.AreEqual(50f, Percent(aegis, aegis.maxRank, StatType.DefensePower), 1e-3);
            Assert.Greater(Percent(aegis, aegis.minRank, StatType.DefensePower), 0f, "every rank gives some");
            Assert.IsTrue(aegis.isAbsorptionShield, "the barrier stays");
            StringAssert.Contains("Defense by 2% - 50%", aegis.description, "the tooltip says so");
        }

        [Test]
        public void EveryGuardGivesTenPercentElementalAttackAtItsTopRank()
        {
            foreach (string path in GuardPaths)
            {
                var guard = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(path);
                Assert.IsNotNull(guard, path);
                Assert.AreEqual(10f, Percent(guard, guard.maxRank, StatType.ElementalAttack), 1e-3, path);
                Assert.AreEqual(10f, Percent(guard, guard.maxRank, StatType.ElementalDefense), 1e-3, path + ": its ward stays");
                StringAssert.Contains("Elemental Attack", guard.description, path);
            }
        }

        [Test]
        public void WithGuardUpTheElementalPartOfAHitIsTenPercentHigher()
        {
            var guard = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(GuardPaths[0]);
            var a = new GameObject("AegisAndGuardTests.attacker").AddComponent<CharacterCombatStats>();
            var d = new GameObject("AegisAndGuardTests.defender").AddComponent<CharacterCombatStats>();
            try
            {
                a.SetSourceModifiers(a, new List<StatModifier>
                {
                    new StatModifier(StatType.AttackPower, ModifierType.Flat, 500f, a),
                    new StatModifier(StatType.ElementalAttack, ModifierType.Flat, 200f, a),
                    new StatModifier(StatType.HitRate, ModifierType.Flat, 1000f, a),
                });
                DamageResult Hit() => CombatCalculator.CalculateAttackInternal(a, d, 1f, 5f, 50f, 99f, 90f, false, 0f, false, 0f);

                float before = Hit().NetElementalDamage;
                Assert.Greater(before, 0f, "the hit has an elemental part");
                // what CharacterBuffManager.ApplyBuff adds for the ward at its top rank
                foreach (StatModifierPayload p in guard.GetStatModifiers(guard.maxRank))
                    a.AddModifier(new StatModifier(p.statType, p.modifierType, p.value, guard));
                Assert.AreEqual(before * 1.1f, Hit().NetElementalDamage, 1e-2);
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(d.gameObject);
            }
        }
    }
}
