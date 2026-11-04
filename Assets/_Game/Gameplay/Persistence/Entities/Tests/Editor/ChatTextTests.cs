using System.Text;
using KryticalNova.Core;
using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>M8 8.4.1: chat on the wire of the entity netcode keeps NGO's messages whole (links, announcements, combat lines) and cuts long text safely.</summary>
    public class ChatTextTests
    {
        [Test]
        public void ALine_WithItsLinks_ComesBackAsItWent()
        {
            var line = new BodyChatLine
            {
                kind = BodyChatKind.Line, channel = ChatChannel.Whisper, outgoing = true, sender = "Corvane", text = "look " + ChatLinks.Marker(0) + ChatLinks.Marker(1),
                linkItems = new ushort[] { 9001, 4210 }, linkData = new ulong[] { 7, ulong.MaxValue },
            };
            BodyChatLine back = ChatText.Line(ChatText.Line(line));
            Assert.AreEqual(line.kind, back.kind);
            Assert.AreEqual(line.channel, back.channel);
            Assert.IsTrue(back.outgoing);
            Assert.AreEqual("Corvane", back.sender);
            Assert.AreEqual(line.text, back.text, "the link markers survive");
            CollectionAssert.AreEqual(line.linkItems, back.linkItems);
            CollectionAssert.AreEqual(line.linkData, back.linkData);
            Assert.IsNull(ChatText.Line(ChatText.Line(new BodyChatLine { kind = BodyChatKind.Line, text = "plain" })).linkItems, "no links: none");
        }

        [Test]
        public void AnAnnouncementAndACombatLine_ComeBackAsTheyWent()
        {
            BodyChatLine a = ChatText.Line(ChatText.Line(new BodyChatLine
            {
                kind = BodyChatKind.Announce, channel = ChatChannel.System, announcement = AnnouncementKind.Drop, sender = "Talwyn", text = "found it",
                highlight = "Talwyn|Storm Edge", rarity = 4,
            }));
            Assert.AreEqual(BodyChatKind.Announce, a.kind);
            Assert.AreEqual(AnnouncementKind.Drop, a.announcement);
            Assert.AreEqual("Talwyn|Storm Edge", a.highlight);
            Assert.AreEqual(4, a.rarity);
            BodyChatLine c = ChatText.Line(ChatText.Line(new BodyChatLine { kind = BodyChatKind.Combat, outgoing = true, sender = "Rat", damage = 12.5f, reflected = 3f, flags = 5 }));
            Assert.AreEqual(BodyChatKind.Combat, c.kind);
            Assert.AreEqual(12.5f, c.damage);
            Assert.AreEqual(3f, c.reflected);
            Assert.AreEqual(5, c.flags);
            Assert.AreEqual("Rat", c.sender);
        }

        [Test]
        public void TextLongerThanTheWire_IsCutOnACharacterBoundary()
        {
            string euros = new string('€', 200);   // NGO's 200 characters, 600 bytes of UTF-8
            string cut = ChatText.Text(euros).ToString();
            Assert.Greater(cut.Length, 100, "most of it goes");
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(cut), 509, "it fits the wire");
            Assert.AreEqual(new string('€', cut.Length), cut, "never half a character");
            string faces = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 200));   // surrogate pairs, 4 bytes each
            string cutFaces = ChatText.Text(faces).ToString();
            Assert.AreEqual(0, cutFaces.Length % 2, "a surrogate pair is never split");
            Assert.AreEqual(faces.Substring(0, cutFaces.Length), cutFaces);
        }

        [Test]
        public void TextPages_FitTheWire_AndJoinBackWhole()
        {
            string text = string.Concat(System.Linq.Enumerable.Repeat("Talwyn\t€ note \U0001F600\n", 120));
            var pages = TextPages.Split(text);
            Assert.Greater(pages.Count, 1, "a long list is several pages");
            foreach (string page in pages) Assert.LessOrEqual(Encoding.UTF8.GetByteCount(page), TextPages.PageBytes, "each fits");
            Assert.AreEqual(text, string.Concat(pages), "joined back, nothing lost or split");
            CollectionAssert.AreEqual(new[] { string.Empty }, TextPages.Split(string.Empty), "an empty list is one empty page");
        }

        [Test]
        public void OwnClicks_AreNewClickNumbers_TheFirstSightOnlySetsTheMark()
        {
            var watch = new OwnClickWatch();
            var a = new PlayerKey(1);
            var b = new PlayerKey(2);
            Assert.IsFalse(watch.IsNewClick(a, 7), "the first look at a player is not a click (it arrived with its last one)");
            Assert.IsFalse(watch.IsNewClick(a, 7), "the same click again: none");
            Assert.IsTrue(watch.IsNewClick(a, 8), "a new number: a click of its own");
            Assert.IsFalse(watch.IsNewClick(b, 8), "another player's first look");
            Assert.IsTrue(watch.IsNewClick(a, 9));
            Assert.IsFalse(watch.IsNewClick(a, 0), "no click number: none");
            watch.Forget(a);
            Assert.IsFalse(watch.IsNewClick(a, 10), "forgotten: a first look again");
        }

        [Test]
        public void GhostJumps_AreNewJumpPlans_TheFirstSightOnlySetsTheMark()
        {
            var watch = new GhostJumpWatch();
            var a = new PlayerKey(1);
            Assert.IsFalse(watch.IsNewJump(a, true, 5, 100), "the first look: the jump it arrived in is not new");
            Assert.IsFalse(watch.IsNewJump(a, true, 5, 100), "the same plan again");
            Assert.IsTrue(watch.IsNewJump(a, true, 5, 160), "a new start under the same click number (a jump the server ordered)");
            Assert.IsTrue(watch.IsNewJump(a, true, 6, 200), "a new click's jump");
            Assert.IsFalse(watch.IsNewJump(a, false, 7, 0), "a walk is not a jump");
            Assert.IsFalse(watch.IsNewJump(a, true, 7, 0), "no start tick: none");
        }

        [Test]
        public void TheSend_KeepsNgosLinkLimit()
        {
            Assert.IsTrue(ChatText.TrySend(ChatChannel.General, "hi", "", null, null, out ChatSendRpc none));
            Assert.AreEqual(0, none.LinkCount);
            Assert.IsTrue(ChatText.TrySend(ChatChannel.General, "x", "", new byte[] { 1, 2, 1 }, new byte[] { 5, 6, 7 }, out ChatSendRpc three));
            Assert.IsTrue(ChatText.TryLinks(three, out byte[] containers, out byte[] cells));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 1 }, containers);
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7 }, cells);
            Assert.IsFalse(ChatText.TrySend(ChatChannel.General, "x", "", new byte[4], new byte[4], out _), "more than ChatLinks.Max: not sent (NGO's RPC refused it)");
            Assert.IsFalse(ChatText.TrySend(ChatChannel.General, "x", "", new byte[2], new byte[1], out _), "mismatched arrays: not sent");
            Assert.IsFalse(ChatText.TryLinks(new ChatSendRpc { LinkCount = 4 }, out _, out _), "a forged count is dropped on the server");
            Assert.IsTrue(ChatText.TryLinks(none, out byte[] c0, out _));
            Assert.IsNull(c0, "no links: none");
        }
    }
}
