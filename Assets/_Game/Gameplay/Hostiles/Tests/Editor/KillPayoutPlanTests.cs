using System.Collections.Generic;
using KryticalNova.Social;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>
    /// Who a Hostile kill pays (wave 3, 12 Nov 2026): solo, the Squad in the map (no distance), the carry rule at 6 levels, an escort
    /// paid by its own gap, and the numbers of the settings assets the game runs on.
    /// </summary>
    public sealed class KillPayoutPlanTests
    {
        private const string GapsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/LevelGapSettings.asset";
        private LevelGapSettings gaps;
        private readonly List<Payout> paid = new List<Payout>();

        [SetUp]
        public void SetUp() => gaps = ScriptableObject.CreateInstance<LevelGapSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(gaps);

        private static PayoutMember M(int level, bool sameMap = true, bool escort = false) =>
            new PayoutMember { Level = level, SameMap = sameMap, IsEscort = escort };

        /// <summary>A Recruit of the holder, near (within 500 m) or beyond.</summary>
        private static PayoutMember R(int level, bool near) =>
            new PayoutMember { Level = level, SameMap = true, IsRecruitOfHolder = true, WithinSponsorRange = near };

        private Payout Of(int index)
        {
            foreach (Payout p in paid) if (p.Index == index) return p;
            Assert.Fail($"member {index} was not paid");
            return default;
        }

        private bool IsPaid(int index) => paid.Exists(p => p.Index == index);

        [Test]
        public void NoSquad_ThePlayerAlone()
        {
            Assert.AreEqual(PayoutOutcome.Solo, KillPayoutPlan.Plan(50, gaps, new[] { M(50) }, 0, paid));
            Assert.AreEqual(PayoutOutcome.Solo, KillPayoutPlan.Plan(50, gaps, new PayoutMember[0], -1, paid));
            Assert.AreEqual(0, paid.Count, "the caller pays the holder");
        }

        [Test]
        public void ASquad_PaysEveryMemberInTheMap_ByOwnGap_WithTheBoost_AndNoOneElsewhere()
        {
            var squad = new[] { M(50), M(48), M(52), M(50, sameMap: false) };
            Assert.AreEqual(PayoutOutcome.Squad, KillPayoutPlan.Plan(50, gaps, squad, 1, paid));
            Assert.AreEqual(3, paid.Count);
            Assert.AreEqual(0, Of(0).Gap);
            Assert.AreEqual(2, Of(1).Gap);
            Assert.AreEqual(-2, Of(2).Gap);
            Assert.IsTrue(Of(0).SquadBoost && Of(1).SquadBoost && Of(2).SquadBoost);
            Assert.IsFalse(IsPaid(3), "a member in another map is paid nothing");
        }

        [Test]
        public void ASquadMember2000mAway_InTheSameZone_IsPaid_AndCountsForTheFullSquadBoost()
        {
            // The record has no distance: the same map is the only Squad condition (as built, HostileKillRewards and
            // PlayerParty.ServerExperienceBoost). A member 2,000 m off is in the map like the others.
            var squad = new[] { M(50), M(50), M(50), M(50), M(50) /* 2,000 m away, same zone */ };
            KillPayoutPlan.Plan(50, gaps, squad, 0, paid);
            Assert.AreEqual(5, paid.Count);
            Assert.IsTrue(Of(4).SquadBoost);
            Assert.AreEqual(0.2f, PartyRules.ExperienceBoost(5, 5), 0.0001f, "all five in the map: the full-Squad boost");
            Assert.AreEqual(0.15f, PartyRules.ExperienceBoost(5, 4), 0.0001f, "one in another map: one step less");
        }

        [Test]
        public void TheCarryRule_At6Levels_PaysAFarLowerMemberByTheHighestsGap()
        {
            // Hostile 50: the level 60 member sees -10 (highest), level 55 sees -5 (5 worse: own), level 54 sees -4 (6 worse: carried).
            var squad = new[] { M(60), M(55), M(54) };
            KillPayoutPlan.Plan(50, gaps, squad, 0, paid);
            Assert.AreEqual(-10, Of(0).Gap);
            Assert.AreEqual(-5, Of(1).Gap, "5 levels worse: own gap");
            Assert.AreEqual(-10, Of(2).Gap, "6 levels worse: the highest's gap");
        }

        [Test]
        public void AnEscortThatDidNotHoldTheKill_IsPaidByOwnGap_WithNoBoost_AndIsNeverTheHighest()
        {
            var squad = new[] { M(40), M(80, escort: true), M(44) };
            KillPayoutPlan.Plan(40, gaps, squad, 0, paid);
            Assert.AreEqual(-40, Of(1).Gap);
            Assert.IsFalse(Of(1).SquadBoost);
            Assert.AreEqual(0, Of(0).Gap, "the Sponsor does not carry its Recruits down");
            Assert.AreEqual(-4, Of(2).Gap);
        }

        // ------------------------------------------------------------ a kill the escorting Sponsor holds (owner pick A, 12 Nov 2026)

        [Test]
        public void ASponsorsKill_PaysItsNearRecruit_ByOwnGap_NeverTheSponsor_AndNoSponsorBonus()
        {
            var squad = new[] { M(90, escort: true), R(48, near: true) };
            Assert.AreEqual(PayoutOutcome.EscortPaysRecruits, KillPayoutPlan.Plan(50, gaps, squad, 0, paid));
            Assert.IsFalse(IsPaid(0), "the Sponsor is paid nothing");
            Assert.AreEqual(2, Of(1).Gap, "the Recruit by its own gap");
            Assert.IsTrue(Of(1).SquadBoost);
            Assert.IsFalse(Of(1).SponsorBonus, "no Sponsor bonus on a kill the Sponsor made");
        }

        [Test]
        public void TwoRecruitsNear_AreEachPaidOnce()
        {
            var squad = new[] { R(50, near: true), M(95, escort: true), R(52, near: true) };
            KillPayoutPlan.Plan(50, gaps, squad, 1, paid);
            Assert.AreEqual(2, paid.Count);
            Assert.AreEqual(1, paid.FindAll(p => p.Index == 0).Count);
            Assert.AreEqual(1, paid.FindAll(p => p.Index == 2).Count);
            Assert.AreEqual(0, Of(0).Gap);
            Assert.AreEqual(-2, Of(2).Gap);
        }

        [Test]
        public void OneRecruitNear_OneBeyond500m_TheFarOneIsPaidAsAnotherMember_WithTheSponsorAsHighest()
        {
            // Hostile 50, Sponsor 80 (gap -30): the near Recruit is paid by its own gap; the far one (gap 0) is 30 worse than the
            // Sponsor, so it takes the Sponsor's gap: grey.
            var squad = new[] { M(80, escort: true), R(50, near: true), R(50, near: false) };
            Assert.AreEqual(PayoutOutcome.EscortPaysRecruits, KillPayoutPlan.Plan(50, gaps, squad, 0, paid));
            Assert.AreEqual(0, Of(1).Gap);
            Assert.AreEqual(-30, Of(2).Gap);
            Assert.AreEqual(0f, LevelGapRules.ExperiencePercent(gaps, Of(2).Gap), "the far Recruit is in the low band");
        }

        [Test]
        public void AMateWhoIsNoRecruit_WithTheSponsor30LevelsAbove_GetsTheLowBand()
        {
            var squad = new[] { M(80, escort: true), R(50, near: true), M(50) };
            KillPayoutPlan.Plan(50, gaps, squad, 0, paid);
            Assert.AreEqual(-30, Of(2).Gap, "counted against the Sponsor's level");
            Assert.AreEqual(0f, LevelGapRules.ExperiencePercent(gaps, Of(2).Gap));
            Assert.IsFalse(Of(2).SponsorBonus);
            Assert.AreEqual(0, Of(1).Gap, "the Recruit keeps its own gap");
        }

        [Test]
        public void NoRecruitNear_PaysNoRecruitBonus_AndTheOthersByTheSponsorsGapAtBest()
        {
            var squad = new[] { M(80, escort: true), R(50, near: false), M(78) };
            Assert.AreEqual(PayoutOutcome.EscortNoRecruitNear, KillPayoutPlan.Plan(50, gaps, squad, 0, paid));
            Assert.IsFalse(IsPaid(0));
            Assert.AreEqual(-30, Of(1).Gap);
            Assert.AreEqual(-28, Of(2).Gap, "2 levels below the Sponsor: own gap");
        }

        [Test]
        public void TheAssets_HoldTheNumbersTheRulesWereWrittenFor()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LevelGapSettings>(GapsPath);
            Assert.NotNull(asset, GapsPath);
            Assert.AreEqual(10f, asset.tagTimeoutSeconds, 0.001f, "the kill is held 10 s without a hit");
            Assert.AreEqual(6, asset.squadCarryLevels, "carry at 6 levels");
            for (int gap = -12; gap <= 12; gap++)
            {
                Assert.AreEqual(LevelGapRules.ExperiencePercent(gaps, gap), LevelGapRules.ExperiencePercent(asset, gap), 0.001f, $"XP % at gap {gap}");
                Assert.AreEqual(LevelGapRules.DropPercent(gaps, gap), LevelGapRules.DropPercent(asset, gap), 0.001f, $"drop % at gap {gap}");
                Assert.AreEqual(LevelGapRules.GearDrops(gaps, gap), LevelGapRules.GearDrops(asset, gap), $"gear at gap {gap}");
            }
            Assert.AreEqual(175f, LevelGapRules.ExperiencePercent(asset, 200), 0.001f);
            Assert.AreEqual(0f, LevelGapRules.ExperiencePercent(asset, -200), 0.001f);
        }
    }
}
