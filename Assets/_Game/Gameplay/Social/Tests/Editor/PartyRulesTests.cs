using KryticalNova.Factions;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Who may join a party: the same faction, within ten levels of every member, while there is room.</summary>
    public sealed class PartyRulesTests
    {
        private static PartyCheck Join(FactionId faction, int level, params int[] memberLevels) =>
            PartyRules.Check(FactionId.Hegemony, memberLevels, faction, level);

        [Test]
        public void AFactionMateWithinTenLevelsMayJoin()
        {
            Assert.AreEqual(PartyCheck.Allowed, Join(FactionId.Hegemony, 20, 20));
            Assert.AreEqual(PartyCheck.Allowed, Join(FactionId.Hegemony, 30, 20));
            Assert.AreEqual(PartyCheck.Allowed, Join(FactionId.Hegemony, 10, 20));
        }

        [Test]
        public void ElevenLevelsApartIsTooFarEitherWay()
        {
            Assert.AreEqual(PartyCheck.LevelTooFar, Join(FactionId.Hegemony, 31, 20));
            Assert.AreEqual(PartyCheck.LevelTooFar, Join(FactionId.Hegemony, 9, 20));
        }

        [Test]
        public void TheNewcomerMustBeInRangeOfEveryMember()
        {
            // Within ten of the level 20 leader, but fifteen above the level 15 member.
            Assert.AreEqual(PartyCheck.LevelTooFar, Join(FactionId.Hegemony, 30, 20, 15));
            Assert.AreEqual(PartyCheck.Allowed, Join(FactionId.Hegemony, 25, 20, 15));
        }

        [Test]
        public void ASponsorHuntingWithItsRecruit_IsLeftOutOfTheLevelRule()
        {
            // The Recruit (30) invites its Sponsor (112): the candidate is free of the rule.
            Assert.AreEqual(PartyCheck.Allowed, PartyRules.Check(FactionId.Hegemony, new[] { 30 }, FactionId.Hegemony, 112, new[] { false }, true));
            // The Sponsor (112) and its Recruit (30) take a level 35 player: only the Recruit's level counts.
            Assert.AreEqual(PartyCheck.Allowed, PartyRules.Check(FactionId.Hegemony, new[] { 112, 30 }, FactionId.Hegemony, 35, new[] { true, false }));
            Assert.AreEqual(PartyCheck.LevelTooFar, PartyRules.Check(FactionId.Hegemony, new[] { 112, 30 }, FactionId.Hegemony, 45, new[] { true, false }));
            // Another faction and a full party are refused all the same.
            Assert.AreEqual(PartyCheck.OtherFaction, PartyRules.Check(FactionId.Hegemony, new[] { 30 }, FactionId.Null, 112, null, true));
            Assert.AreEqual(PartyCheck.Full, PartyRules.Check(FactionId.Hegemony, new[] { 30, 30, 30, 30, 30 }, FactionId.Hegemony, 112, null, true));
        }

        [Test]
        public void TheFullBoost_IsANumberThatCanBeTuned()
        {
            Assert.AreEqual(0.4f, PartyRules.ExperienceBoost(5, 5, 0.4f), 1e-5f);
            Assert.AreEqual(0.1f, PartyRules.ExperienceBoost(5, 2, 0.4f), 1e-5f);
            Assert.AreEqual(0f, PartyRules.ExperienceBoost(5, 5, -1f));
        }

        [Test]
        public void OtherFactionsAndTheFactionlessCannotJoin()
        {
            Assert.AreEqual(PartyCheck.OtherFaction, Join(FactionId.Null, 20, 20));
            Assert.AreEqual(PartyCheck.OtherFaction, Join(FactionId.Covenant, 20, 20));
            Assert.AreEqual(PartyCheck.OtherFaction, Join(FactionId.None, 20, 20));
            Assert.AreEqual(PartyCheck.OtherFaction, PartyRules.Check(FactionId.None, new[] { 20 }, FactionId.None, 20));
        }

        [Test]
        public void OnlyAFullPartyAllInTheSameMapEarnsTheFullBoost()
        {
            Assert.AreEqual(0.2f, PartyRules.ExperienceBoost(5, 5), 1e-5f);
            Assert.AreEqual(0.15f, PartyRules.ExperienceBoost(5, 4), 1e-5f);
            Assert.AreEqual(0.05f, PartyRules.ExperienceBoost(5, 2), 1e-5f);
            // A smaller party never reaches the full boost, even all together.
            Assert.AreEqual(0.15f, PartyRules.ExperienceBoost(4, 4), 1e-5f);
            Assert.AreEqual(0.05f, PartyRules.ExperienceBoost(2, 2), 1e-5f);
        }

        [Test]
        public void NoBoostAloneOrWithNoPartyMemberInTheMap()
        {
            Assert.AreEqual(0f, PartyRules.ExperienceBoost(1, 1));
            Assert.AreEqual(0f, PartyRules.ExperienceBoost(0, 0));
            Assert.AreEqual(0f, PartyRules.ExperienceBoost(5, 1));
        }

        [Test]
        public void AFullPartyTakesNobody()
        {
            Assert.AreEqual(PartyCheck.Allowed, Join(FactionId.Hegemony, 20, 20, 20, 20, 20));
            Assert.AreEqual(PartyCheck.Full, Join(FactionId.Hegemony, 20, 20, 20, 20, 20, 20));
        }
    }
}
