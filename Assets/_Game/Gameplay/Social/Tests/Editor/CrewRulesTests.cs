using System.Collections.Generic;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class CrewRulesTests
    {
        private const long Now = 1000;
        private const int MaxOfficers = 2;
        private Crew crew;

        private static CrewMember Member(string id, string name) => new CrewMember { characterId = id, name = name };

        [SetUp]
        public void SetUp()
        {
            crew = CrewRules.Found("crew1", "Night Shift", 1, 3, Member("a", "Tolan"), Now);
            CrewRules.Join(crew, Member("b", "Vex"), Now);
            CrewRules.Join(crew, Member("c", "Kael"), Now);
            CrewRules.Join(crew, Member("d", "Mira"), Now);
        }

        [Test]
        public void TheFounderLeads_AndEveryoneElseJoinsAsAMember()
        {
            Assert.AreEqual(CrewRank.Leader, CrewRules.Find(crew, "a").Rank);
            Assert.AreEqual(CrewRank.Member, CrewRules.Find(crew, "b").Rank);
            Assert.AreEqual(1, CrewRules.Count(crew, CrewRank.Leader));
            Assert.AreEqual(4, crew.members.Count);
        }

        [Test]
        public void ACrewName_IsTidied_Bounded_PlainAndUnique()
        {
            var taken = new List<string> { "Night Shift" };
            Assert.IsNull(CrewRules.NameBlockedReason("  Iron   Lotus ", 3, 16, taken, out string clean));
            Assert.AreEqual("Iron Lotus", clean);
            Assert.IsNotNull(CrewRules.NameBlockedReason("ab", 3, 16, taken, out _));
            Assert.IsNotNull(CrewRules.NameBlockedReason("A Name That Goes On Far Too Long", 3, 16, taken, out _));
            Assert.IsNotNull(CrewRules.NameBlockedReason("night shift", 3, 16, taken, out _), "names differ by more than case");
            Assert.IsNotNull(CrewRules.NameBlockedReason("Bad!Name", 3, 16, taken, out _));
            Assert.IsNull(CrewRules.NameBlockedReason("O'Neil-7", 3, 16, taken, out _));
            CrewRules.NameBlockedReason("<b>Bold</b>", 3, 16, taken, out string stripped);
            Assert.IsFalse(stripped.Contains("<"));
        }

        [Test]
        public void FoundingTakesTheLevelAndTheCreds_AndNoOtherCrew()
        {
            Assert.IsNull(CrewRules.FoundBlockedReason(false, 30, 30, 10_000_000, 10_000_000));
            Assert.IsNotNull(CrewRules.FoundBlockedReason(false, 29, 30, 10_000_000, 10_000_000));
            Assert.IsNotNull(CrewRules.FoundBlockedReason(false, 30, 30, 9_999_999, 10_000_000));
            Assert.AreEqual(CrewRules.ReasonAlreadyInCrew, CrewRules.FoundBlockedReason(true, 99, 30, long.MaxValue, 10_000_000));
        }

        [Test]
        public void TheMemberCap_FollowsTheCrewLevel_AndHoldsAtTheLast()
        {
            int[] caps = { 20, 30, 40 };
            Assert.AreEqual(20, CrewRules.MemberCap(caps, 1));
            Assert.AreEqual(30, CrewRules.MemberCap(caps, 2));
            Assert.AreEqual(40, CrewRules.MemberCap(caps, 9));
            Assert.AreEqual(20, CrewRules.MemberCap(caps, 0));
            Assert.AreEqual(20, CrewRules.MemberCap(null, 1));
        }

        [Test]
        public void OnlyTheLeaderAndOfficersInvite_IntoAFactionCrewWithRoom()
        {
            Assert.AreEqual(CrewRules.ReasonOfficerOnly, CrewRules.InviteBlockedReason(crew, CrewRank.Member, 20, 1, false, "Ash"));
            Assert.IsNull(CrewRules.InviteBlockedReason(crew, CrewRank.Officer, 20, 1, false, "Ash"));
            Assert.AreEqual(CrewRules.OtherFaction("Ash"), CrewRules.InviteBlockedReason(crew, CrewRank.Leader, 20, 2, false, "Ash"), "another faction, whatever the rank");
            Assert.IsNotNull(CrewRules.InviteBlockedReason(crew, CrewRank.Leader, 20, 1, true, "Ash"), "already in a Crew");
            Assert.IsNotNull(CrewRules.InviteBlockedReason(crew, CrewRank.Leader, 4, 1, false, "Ash"), "full");
        }

        [Test]
        public void TheLeaderExpelsAnyoneElse_AnOfficerOnlyMembers_AMemberNobody()
        {
            Assert.IsTrue(CrewRules.CanExpel(CrewRank.Leader, CrewRank.Officer));
            Assert.IsTrue(CrewRules.CanExpel(CrewRank.Leader, CrewRank.Member));
            Assert.IsFalse(CrewRules.CanExpel(CrewRank.Leader, CrewRank.Leader));
            Assert.IsTrue(CrewRules.CanExpel(CrewRank.Officer, CrewRank.Member));
            Assert.IsFalse(CrewRules.CanExpel(CrewRank.Officer, CrewRank.Officer));
            Assert.IsFalse(CrewRules.CanExpel(CrewRank.Officer, CrewRank.Leader));
            Assert.IsFalse(CrewRules.CanExpel(CrewRank.Member, CrewRank.Member));
        }

        [Test]
        public void OnlyTheLeaderAppointsOfficers_UpToTheLimit()
        {
            CrewMember vex = CrewRules.Find(crew, "b"), kael = CrewRules.Find(crew, "c"), mira = CrewRules.Find(crew, "d");
            Assert.AreEqual(CrewRules.ReasonLeaderOnly, CrewRules.OfficerBlockedReason(crew, CrewRank.Officer, vex, true, MaxOfficers));
            Assert.IsNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, vex, true, MaxOfficers));
            vex.rank = (int)CrewRank.Officer;
            Assert.IsNotNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, vex, true, MaxOfficers), "already one");
            kael.rank = (int)CrewRank.Officer;
            Assert.IsNotNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, mira, true, MaxOfficers), "no room for a third");
            Assert.IsNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, vex, false, MaxOfficers));
            Assert.IsNotNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, mira, false, MaxOfficers), "not an Officer");
            Assert.IsNotNull(CrewRules.OfficerBlockedReason(crew, CrewRank.Leader, CrewRules.Find(crew, "a"), true, MaxOfficers), "the Leader");
        }

        [Test]
        public void HandingOver_MakesANewLeader_AndTheOldOneAnOfficer()
        {
            Assert.IsTrue(CrewRules.TryHandOver(crew, "a", "c", MaxOfficers, out _));
            Assert.AreEqual(CrewRank.Leader, CrewRules.Find(crew, "c").Rank);
            Assert.AreEqual(CrewRank.Officer, CrewRules.Find(crew, "a").Rank);
            Assert.AreEqual(1, CrewRules.Count(crew, CrewRank.Leader));
            Assert.IsFalse(CrewRules.TryHandOver(crew, "a", "b", MaxOfficers, out string reason), "no longer the Leader");
            Assert.AreEqual(CrewRules.ReasonLeaderOnly, reason);
            Assert.IsFalse(CrewRules.TryHandOver(crew, "c", "c", MaxOfficers, out _), "not to yourself");
        }

        [Test]
        public void ALeaderWithOthersCannotWalkOut_TheLastOneCan()
        {
            Assert.AreEqual(CrewRules.ReasonLeaderMustHandOver, CrewRules.LeaveBlockedReason(CrewRank.Leader, 4));
            Assert.IsNull(CrewRules.LeaveBlockedReason(CrewRank.Leader, 1));
            Assert.IsNull(CrewRules.LeaveBlockedReason(CrewRank.Officer, 4));
            Assert.IsNull(CrewRules.LeaveBlockedReason(CrewRank.Member, 4));
            Assert.IsTrue(CrewRules.Remove(crew, "d"));
            Assert.IsNull(CrewRules.Find(crew, "d"));
            Assert.IsFalse(CrewRules.Remove(crew, "d"));
        }

        [Test]
        public void TitlesAndAnnouncements_AreOnePlainLine_OfBoundedLength()
        {
            Assert.AreEqual("Quartermaster", CrewRules.CleanTitle("  Quartermaster  ", 16));
            Assert.AreEqual("Quartermaste", CrewRules.CleanTitle("Quartermaster", 12));
            Assert.AreEqual("War at dawn", CrewRules.CleanAnnouncement("War\n at   dawn", 160));
            Assert.IsFalse(CrewRules.CleanAnnouncement("<size=99>big</size>", 160).Contains("<"));
            Assert.AreEqual(160, CrewRules.CleanAnnouncement(new string('x', 400), 160).Length);
            Assert.AreEqual(string.Empty, CrewRules.CleanTitle(null, 12));
        }

        [Test]
        public void TheRoster_ReadsLeaderFirst_ThenOfficers_OnlineBeforeOffline_ThenByName()
        {
            Assert.Less(CrewRules.Compare(CrewRank.Leader, false, "Zed", CrewRank.Officer, true, "Abe"), 0);
            Assert.Less(CrewRules.Compare(CrewRank.Member, true, "Zed", CrewRank.Member, false, "Abe"), 0);
            Assert.Less(CrewRules.Compare(CrewRank.Member, true, "abe", CrewRank.Member, true, "Zed"), 0);
        }
    }
}
