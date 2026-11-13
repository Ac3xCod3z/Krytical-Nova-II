using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17, item 7.4 (owner, 13 Nov 2026): a Stance given to Squad mates gives them only the Squad's share of it
    /// (<c>SquadSettings.sharedBuffPercent</c>, 50 proposed); the caster gets all of it.
    /// </summary>
    public sealed class SquadBuffShareTests
    {
        private sealed class Squad : MonoBehaviour, ISquadBuffTargets
        {
            public float Share = 50f;
            public int BuffShareRank => 20;
            public float BuffShareRadius => 20f;
            public float BuffSharePercent => Share;
            public string BuffGiverName => "Tester";
            public void ServerGetBuffTargets(System.Collections.Generic.List<PlayerCombatSkills> into) { }
        }

        [Test]
        public void TheScaleIsWholeOnTheCasterAndTheShareOnAMate()
        {
            Assert.AreEqual(20f, SquadBuffRules.Scaled(20f, fromSquadmate: false, sharePercent: 50f));
            Assert.AreEqual(10f, SquadBuffRules.Scaled(20f, fromSquadmate: true, sharePercent: 50f));
            Assert.AreEqual(0f, SquadBuffRules.Scaled(20f, fromSquadmate: true, sharePercent: -5f));
            Assert.AreEqual(20f, SquadBuffRules.Scaled(20f, fromSquadmate: true, sharePercent: 150f), "never more than the caster's");
        }

        [Test]
        public void TheCasterGetsAllOfTheStanceAndAMateTheShare()
        {
            var stance = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            stance.skillId = "TestStance";
            stance.sharesWithSquad = true;
            stance.maxDamagePercent = 0f;
            stance.minRank = 1;
            stance.maxRank = 1;
            stance.rankScaledModifiers = new[] { new RankScaledModifier(StatType.AttackPower, ModifierType.PercentAdd, 20f, 20f) };
            var caster = new GameObject("SquadBuffShareTests.caster");
            var mate = new GameObject("SquadBuffShareTests.mate");
            try
            {
                foreach (GameObject body in new[] { caster, mate })
                {
                    body.AddComponent<CharacterCombatStats>();
                    body.AddComponent<Squad>();
                    body.AddComponent<CharacterBuffManager>();
                }
                Assert.IsTrue(caster.GetComponent<CharacterBuffManager>().ApplyBuff(stance, 1));
                Assert.IsTrue(mate.GetComponent<CharacterBuffManager>().ApplyBuff(stance, 1, fromSquadmate: true, giver: "Tester"));

                float Bonus(GameObject body)
                {
                    float sum = 0f;
                    foreach (StatModifier mod in body.GetComponent<CharacterBuffManager>().GetBuff("TestStance").AppliedModifiers) sum += mod.Value;
                    return sum;
                }
                Assert.AreEqual(20f, Bonus(caster), 1e-4, "the caster: 100 %");
                Assert.AreEqual(10f, Bonus(mate), 1e-4, "a Squad mate: the set 50 %");
                StringAssert.Contains("50% of its strength (shared)", SquadBuffRules.TooltipLine(stance, 20, 20, 20f, 50f));
            }
            finally
            {
                Object.DestroyImmediate(caster);
                Object.DestroyImmediate(mate);
                Object.DestroyImmediate(stance);
            }
        }
    }
}
