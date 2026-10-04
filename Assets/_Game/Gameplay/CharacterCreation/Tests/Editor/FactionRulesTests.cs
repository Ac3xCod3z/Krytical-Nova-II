using KryticalNova.Factions;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Who may attack whom between players: the faction rule and the GM stance pins.</summary>
    public sealed class FactionRulesTests
    {
        private sealed class Member : IFactionMember
        {
            public FactionId Faction { get; set; }
            public PvpStance Stance { get; set; }
        }

        private bool saved;

        [SetUp]
        public void Remember() => saved = FactionRules.CrossFactionPvp;

        [TearDown]
        public void Restore() => FactionRules.CrossFactionPvp = saved;

        private static Member Of(FactionId faction, PvpStance stance = PvpStance.Faction) => new Member { Faction = faction, Stance = stance };

        [Test]
        public void OtherFactionsAreHostileWhileTheSwitchIsOn()
        {
            FactionRules.CrossFactionPvp = true;
            Assert.IsTrue(FactionRules.AreHostile(Of(FactionId.Hegemony), Of(FactionId.Null)));
            Assert.IsTrue(FactionRules.AreHostile(Of(FactionId.Covenant), Of(FactionId.Hegemony)));
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.Null), Of(FactionId.Null)), "same faction");
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.None), Of(FactionId.Null)), "no faction, no feud");
        }

        [Test]
        public void NobodyIsHostileWhileTheSwitchIsOff()
        {
            FactionRules.CrossFactionPvp = false;
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.Hegemony), Of(FactionId.Null)));
        }

        [Test]
        public void AFriendlyPinProtectsBothWays()
        {
            FactionRules.CrossFactionPvp = true;
            var pinned = Of(FactionId.Hegemony, PvpStance.Friendly);
            var enemy = Of(FactionId.Null);
            Assert.IsFalse(FactionRules.AreHostile(pinned, enemy));
            Assert.IsFalse(FactionRules.AreHostile(enemy, pinned));
            Assert.IsFalse(FactionRules.AreHostile(pinned, Of(FactionId.Covenant, PvpStance.Hostile)), "friendly wins over hostile");
        }

        [Test]
        public void AHostilePinFightsEveryoneEvenWithTheSwitchOff()
        {
            FactionRules.CrossFactionPvp = false;
            var pinned = Of(FactionId.Hegemony, PvpStance.Hostile);
            Assert.IsTrue(FactionRules.AreHostile(pinned, Of(FactionId.Hegemony)), "own faction");
            Assert.IsTrue(FactionRules.AreHostile(Of(FactionId.Covenant), pinned));
            Assert.IsFalse(FactionRules.AreHostile(pinned, pinned), "never hostile to yourself");
        }

        [Test]
        public void TheCatalogDefaultIsTakenOnlyOncePerSession()
        {
            // Whatever the session already configured, a later Configure must not undo a GM's switch.
            FactionRules.Configure(true);
            FactionRules.CrossFactionPvp = false;
            FactionRules.Configure(true);
            Assert.IsFalse(FactionRules.CrossFactionPvp);
        }
    }
}
