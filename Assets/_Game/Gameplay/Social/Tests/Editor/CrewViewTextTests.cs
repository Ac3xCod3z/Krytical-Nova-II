using KryticalNova.Social;
using NUnit.Framework;
using Unity.Collections;

namespace KryticalNova.Tests
{
    /// <summary>M8 8.4.5: the Crew window as text (the entity netcode's pages) comes back as NGO's ViewRpc sent it.</summary>
    public sealed class CrewViewTextTests
    {
        [Test]
        public void TheHeaderAndEveryMember_ComeBack_EvenATabInTheAnnouncement()
        {
            var header = new CrewHeader { exists = true, symbol = 4, colour = 7, memberCap = 30, myRank = CrewRank.Officer };
            header.name.CopyFromTruncated("Ironclad");
            header.announcement.CopyFromTruncated("Muster\tat dawn \\ east");
            var a = new CrewMemberView { rank = CrewRank.Leader, level = 112, weaponClass = 3, online = true, lastSeenUtc = 638_000_000_000_000_000 };
            a.id.CopyFromTruncated("c1");
            a.name.CopyFromTruncated("Talwyn");
            a.title.CopyFromTruncated("Warden");
            var b = new CrewMemberView { rank = CrewRank.Member, level = 7, weaponClass = 0, online = false, lastSeenUtc = 5 };
            b.id.CopyFromTruncated("c2");
            b.name.CopyFromTruncated("Brannoc");
            Assert.IsTrue(CrewViewText.TryDecode(CrewViewText.Encode(header, new[] { a, b }), out CrewHeader h, out CrewMemberView[] m));
            Assert.IsTrue(h.exists);
            Assert.AreEqual(header.name, h.name);
            Assert.AreEqual(header.announcement, h.announcement);
            Assert.AreEqual(4, h.symbol);
            Assert.AreEqual(7, h.colour);
            Assert.AreEqual(30, h.memberCap);
            Assert.AreEqual(CrewRank.Officer, h.myRank);
            Assert.AreEqual(2, m.Length);
            Assert.AreEqual(a.name, m[0].name);
            Assert.AreEqual(a.title, m[0].title);
            Assert.AreEqual(CrewRank.Leader, m[0].rank);
            Assert.AreEqual(112, m[0].level);
            Assert.IsTrue(m[0].online);
            Assert.AreEqual(a.lastSeenUtc, m[0].lastSeenUtc);
            Assert.AreEqual(b.id, m[1].id);
            Assert.IsFalse(m[1].online);
        }

        [Test]
        public void NoCrew_IsAnEmptyHeader_AndBrokenTextIsRefused()
        {
            Assert.IsTrue(CrewViewText.TryDecode(CrewViewText.Encode(default, null), out CrewHeader h, out CrewMemberView[] m));
            Assert.IsFalse(h.exists);
            Assert.AreEqual(0, m.Length);
            Assert.IsFalse(CrewViewText.TryDecode("1\tIronclad", out _, out _));
            Assert.IsFalse(CrewViewText.TryDecode(string.Empty, out _, out _));
        }
    }
}
