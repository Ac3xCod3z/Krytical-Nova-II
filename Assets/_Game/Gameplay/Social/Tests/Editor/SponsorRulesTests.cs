using KryticalNova.Factions;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class SponsorRulesTests
    {
        private const int MinLevel = 112, Gap = 5, MaxRecruits = 3;

        private static string Blocked(int sponsorLevel, int recruitLevel, FactionId sponsorFaction = FactionId.Hegemony,
            FactionId recruitFaction = FactionId.Hegemony, int recruits = 0, bool sponsorIsRecruit = false, bool recruitHasSponsor = false,
            bool recruitIsSponsor = false) =>
            SponsorRules.LinkBlockedReason(sponsorLevel, recruitLevel, sponsorFaction, recruitFaction, recruits, sponsorIsRecruit,
                recruitHasSponsor, recruitIsSponsor, MinLevel, Gap, MaxRecruits);

        [Test]
        public void TheOneAtTheSponsorLevel_IsTheSponsor()
        {
            Assert.IsTrue(SponsorRules.TryRoles(112, 40, MinLevel, out bool aSponsors));
            Assert.IsTrue(aSponsors);
            Assert.IsTrue(SponsorRules.TryRoles(40, 112, MinLevel, out aSponsors));
            Assert.IsFalse(aSponsors);
            Assert.IsFalse(SponsorRules.TryRoles(60, 40, MinLevel, out _), "neither can sponsor");
            Assert.IsFalse(SponsorRules.TryRoles(112, 112, MinLevel, out _), "two Sponsors are not a Sponsor and a Recruit");
        }

        [Test]
        public void ARecruit_MustBeMoreThanTheGapBelowItsSponsor()
        {
            Assert.IsNull(Blocked(112, 106), "six below");
            StringAssert.Contains("more than 5 levels", Blocked(112, 107), "five below is caught up");
            StringAssert.Contains("more than 5 levels", Blocked(112, 112));
        }

        [Test]
        public void TheLinkEnds_WhenTheRecruitClosesToWithinTheGap()
        {
            Assert.IsFalse(SponsorRules.Outgrown(112, 106, Gap));
            Assert.IsTrue(SponsorRules.Outgrown(112, 107, Gap));
            Assert.IsTrue(SponsorRules.Outgrown(112, 120, Gap), "a Recruit above the Sponsor has long caught up");
        }

        [Test]
        public void ASponsor_MustHaveReachedTheSponsorLevel_InTheSameFaction()
        {
            StringAssert.Contains("level 112", Blocked(111, 20));
            Assert.AreEqual(SponsorRules.ReasonOtherFaction, Blocked(112, 20, FactionId.Hegemony, FactionId.Null));
            Assert.AreEqual(SponsorRules.ReasonOtherFaction, Blocked(112, 20, FactionId.None, FactionId.None), "a character with no faction has no Sponsor");
        }

        [Test]
        public void OneSponsorEach_AndOnlySoManyRecruits()
        {
            Assert.AreEqual(SponsorRules.ReasonHasSponsor, Blocked(112, 20, recruitHasSponsor: true));
            Assert.AreEqual(SponsorRules.ReasonIsRecruit, Blocked(112, 20, sponsorIsRecruit: true));
            Assert.AreEqual(SponsorRules.ReasonIsSponsor, Blocked(112, 20, recruitIsSponsor: true));
            Assert.IsNull(Blocked(112, 20, recruits: MaxRecruits - 1));
            StringAssert.Contains("3 Recruits", Blocked(112, 20, recruits: MaxRecruits));
        }

        [Test]
        public void OutsideTheSponsorsSquad_TheBonusDoesNotAddToTheSquadBoost()
        {
            Assert.AreEqual(0.5f, SponsorRules.ExperienceBoost(0.2f, 0.5f), "the larger counts");
            Assert.AreEqual(0.2f, SponsorRules.ExperienceBoost(0.2f, 0f), "no Sponsor near: the Squad's");
            Assert.AreEqual(0.6f, SponsorRules.ExperienceBoost(0.6f, 0.5f));
            Assert.AreEqual(0f, SponsorRules.ExperienceBoost(-1f, -1f));
        }

        [Test]
        public void InTheSponsorsSquad_TheBonusAddsToTheSquadBoost()
        {
            Assert.AreEqual(0.4f, SponsorRules.ExperienceBoost(0.2f, 0.2f, adds: true), 1e-5f, "a full Squad with all three Recruits");
            Assert.AreEqual(0.1f, SponsorRules.ExperienceBoost(0f, 0.1f, adds: true), 1e-5f);
            Assert.AreEqual(0.2f, SponsorRules.ExperienceBoost(0.2f, -1f, adds: true), 1e-5f);
        }

        [Test]
        public void TheBonus_GrowsWithTheRecruitsHuntingTogether()
        {
            var steps = new[] { 0.1f, 0.15f, 0.2f };
            Assert.AreEqual(0.1f, SponsorRules.Bonus(steps, 1));
            Assert.AreEqual(0.15f, SponsorRules.Bonus(steps, 2));
            Assert.AreEqual(0.2f, SponsorRules.Bonus(steps, 3));
            Assert.AreEqual(0.2f, SponsorRules.Bonus(steps, 9), "the last step holds for more");
            Assert.AreEqual(0f, SponsorRules.Bonus(steps, 0));
            Assert.AreEqual(0f, SponsorRules.Bonus(null, 2));
            Assert.AreEqual(0f, SponsorRules.Bonus(new float[0], 2));
        }

        [Test]
        public void ASponsor_GetsNoDropsFromHostilesFarBelowIt()
        {
            Assert.IsTrue(SponsorRules.SponsorDropsNothing(hostileLevel: 60, sponsorLevel: 112, noDropLevels: 5));
            Assert.IsTrue(SponsorRules.SponsorDropsNothing(107, 112, 5), "five below is far enough");
            Assert.IsFalse(SponsorRules.SponsorDropsNothing(108, 112, 5));
            Assert.IsFalse(SponsorRules.SponsorDropsNothing(115, 112, 5));
        }

        [Test]
        public void SponsorPoints_ArePaidForEachFullMinuteNear()
        {
            float near = 59f;
            Assert.AreEqual(0, SponsorRules.PointsDue(ref near, 250));
            Assert.AreEqual(59f, near);
            near = 125f;
            Assert.AreEqual(500, SponsorRules.PointsDue(ref near, 250));
            Assert.AreEqual(5f, near, 0.001f, "what is left of the third minute is kept");
            near = 200f;
            Assert.AreEqual(0, SponsorRules.PointsDue(ref near, 0), "no points are set");
        }
    }
}
