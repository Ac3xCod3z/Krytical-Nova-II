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
