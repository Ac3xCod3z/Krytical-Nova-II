using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A Stance of rank 20 or more reaches the Squad members within 20 m; the same Stance never stacks, and given
    /// again it restarts its timer at the higher rank (owner, 16 Oct 2026).
    /// </summary>
    public sealed class SquadBuffRulesTests
    {
        private SkillDefinitionSO stance;
        private SkillDefinitionSO guard;

        [SetUp]
        public void SetUp()
        {
            stance = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            stance.sharesWithSquad = true;
            stance.maxDamagePercent = 0f;
            guard = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            guard.maxDamagePercent = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stance);
            Object.DestroyImmediate(guard);
        }

        [Test]
        public void AStanceReachesTheSquad_FromRank20_AnythingElseNever()
        {
            Assert.IsFalse(SquadBuffRules.Shares(stance, 19, 20), "below rank 20 it is the caster's own");
            Assert.IsTrue(SquadBuffRules.Shares(stance, 20, 20));
            Assert.IsTrue(SquadBuffRules.Shares(stance, 28, 20));
            Assert.IsFalse(SquadBuffRules.Shares(guard, 28, 20), "a buff that is not shared");
            Assert.IsFalse(SquadBuffRules.Shares(null, 28, 20));
        }

        [Test]
        public void OnlyMembersWithin20m_AreReached()
        {
            Assert.IsTrue(SquadBuffRules.InReach(Vector3.zero, new Vector3(20f, 0f, 0f), 20f));
            Assert.IsTrue(SquadBuffRules.InReach(Vector3.zero, new Vector3(12f, 12f, 0f), 20f), "up a tree, still near");
            Assert.IsFalse(SquadBuffRules.InReach(Vector3.zero, new Vector3(20.1f, 0f, 0f), 20f));
        }

        [Test]
        public void GivenAgain_ItRestartsAtTheHigherRank_NeverStacks()
        {
            Assert.AreEqual(12, SquadBuffRules.RankAfterRecast(false, 0, 12), "a first cast is what it is");
            Assert.AreEqual(25, SquadBuffRules.RankAfterRecast(true, 25, 20), "a weaker cast does not weaken it");
            Assert.AreEqual(28, SquadBuffRules.RankAfterRecast(true, 20, 28));
        }

        [Test]
        public void TheTooltipSaysSo_BeforeAndAfterRank20()
        {
            StringAssert.Contains("From rank 20", SquadBuffRules.TooltipLine(stance, 5, 20, 20f));
            StringAssert.Contains("within 20 m", SquadBuffRules.TooltipLine(stance, 5, 20, 20f));
            StringAssert.Contains("Now also given", SquadBuffRules.TooltipLine(stance, 20, 20, 20f));
            StringAssert.Contains("does not stack", SquadBuffRules.TooltipLine(stance, 20, 20, 20f));
            Assert.AreEqual(string.Empty, SquadBuffRules.TooltipLine(guard, 20, 20, 20f));
        }
    }
}
