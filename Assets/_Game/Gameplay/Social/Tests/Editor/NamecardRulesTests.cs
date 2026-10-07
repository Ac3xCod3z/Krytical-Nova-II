using KryticalNova.Factions;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class NamecardRulesTests
    {
        [Test]
        public void YourOwnNamecardFollowsYourSetting_WhereverYouStand()
        {
            Assert.IsTrue(NamecardRules.Visible(true, true, FactionId.Hegemony, FactionId.Hegemony, false));
            Assert.IsFalse(NamecardRules.Visible(true, false, FactionId.Hegemony, FactionId.Hegemony, false));
            Assert.IsFalse(NamecardRules.Visible(true, false, FactionId.Hegemony, FactionId.Hegemony, true), "neutral ground does not force it on");
        }

        [Test]
        public void YourFactionAlwaysSeesYou()
        {
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
            {
                Assert.IsTrue(NamecardRules.Visible(false, true, faction, faction, false), $"{faction}, off neutral ground");
                Assert.IsTrue(NamecardRules.Visible(false, false, faction, faction, true), $"{faction}: the 'own namecard' setting is only about your own");
            }
        }

        [Test]
        public void AnotherFactionSeesYouOnlyOnNeutralGround()
        {
            Assert.IsFalse(NamecardRules.Visible(false, true, FactionId.Hegemony, FactionId.Covenant, false));
            Assert.IsTrue(NamecardRules.Visible(false, true, FactionId.Hegemony, FactionId.Covenant, true));
            Assert.IsFalse(NamecardRules.Visible(false, true, FactionId.Null, FactionId.Hegemony, false));
            Assert.IsTrue(NamecardRules.Visible(false, true, FactionId.Null, FactionId.Hegemony, true));
        }
    }
}
