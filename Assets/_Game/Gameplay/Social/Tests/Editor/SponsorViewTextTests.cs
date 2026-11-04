using KryticalNova.Social;
using NUnit.Framework;
using Unity.Collections;

namespace KryticalNova.Tests
{
    /// <summary>M8 8.4.4: the Sponsor window's links as text (the entity netcode's pages) come back as NGO's ViewRpc sent them.</summary>
    public sealed class SponsorViewTextTests
    {
        [Test]
        public void EveryLink_ComesBack()
        {
            var a = new SponsorLinkView { level = 112, isSponsor = true, online = true, near = false };
            a.characterId.CopyFromTruncated("c1");
            a.name.CopyFromTruncated("Talwyn");
            var b = new SponsorLinkView { level = 7, isSponsor = false, online = false, near = true };
            b.characterId.CopyFromTruncated("c2");
            b.name.CopyFromTruncated("Brannoc");
            Assert.IsTrue(SponsorViewText.TryDecode(SponsorViewText.Encode(new[] { a, b }), out SponsorLinkView[] back));
            Assert.AreEqual(2, back.Length);
            Assert.AreEqual(a.characterId, back[0].characterId);
            Assert.AreEqual(a.name, back[0].name);
            Assert.AreEqual(112, back[0].level);
            Assert.IsTrue(back[0].isSponsor && back[0].online && !back[0].near);
            Assert.AreEqual(b.name, back[1].name);
            Assert.IsTrue(!back[1].isSponsor && !back[1].online && back[1].near);
            Assert.IsTrue(SponsorViewText.TryDecode(string.Empty, out SponsorLinkView[] none));
            Assert.AreEqual(0, none.Length);
            Assert.IsFalse(SponsorViewText.TryDecode("c1\tTalwyn", out _), "broken text is refused");
        }
    }
}
