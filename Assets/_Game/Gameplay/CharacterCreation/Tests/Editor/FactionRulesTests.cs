using System.Collections.Generic;
using KryticalNova.Factions;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Who may attack whom between players: the faction rule, and the duel that is its one exception.</summary>
    public sealed class FactionRulesTests
    {
        private sealed class Member : IFactionMember
        {
            private readonly HashSet<IFactionMember> duels = new HashSet<IFactionMember>();
            public FactionId Faction { get; set; }
            public bool IsDuelling(IFactionMember other) => duels.Contains(other);

            public static void Duel(Member a, Member b)
            {
                a.duels.Add(b);
                b.duels.Add(a);
            }
        }

        private bool saved;

        [SetUp]
        public void Remember() => saved = FactionRules.CrossFactionPvp;

        [TearDown]
        public void Restore() => FactionRules.CrossFactionPvp = saved;

        private static Member Of(FactionId faction) => new Member { Faction = faction };

        [Test]
        public void OtherFactionsAreHostileWhileTheSwitchIsOn()
        {
            FactionRules.CrossFactionPvp = true;
            Assert.IsTrue(FactionRules.AreHostile(Of(FactionId.Hegemony), Of(FactionId.Null)));
            Assert.IsTrue(FactionRules.AreHostile(Of(FactionId.Covenant), Of(FactionId.Hegemony)));
        }

        [Test]
        public void TheSameFactionIsAlwaysFriendly()
        {
            FactionRules.CrossFactionPvp = true;
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.Null), Of(FactionId.Null)));
            FactionRules.CrossFactionPvp = false;
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.Null), Of(FactionId.Null)));
        }

        [Test]
        public void NobodyIsHostileWhileTheSwitchIsOff()
        {
            FactionRules.CrossFactionPvp = false;
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.Hegemony), Of(FactionId.Null)));
        }

        [Test]
        public void PlayersWithoutAFactionFightNobody()
        {
            FactionRules.CrossFactionPvp = true;
            Assert.IsFalse(FactionRules.AreHostile(Of(FactionId.None), Of(FactionId.Hegemony)));
        }

        [Test]
        public void ADuelMakesTwoFactionMatesHostileToEachOther()
        {
            FactionRules.CrossFactionPvp = true;
            Member a = Of(FactionId.Null), b = Of(FactionId.Null);
            Assert.IsFalse(FactionRules.AreHostile(a, b));
            Member.Duel(a, b);
            Assert.IsTrue(FactionRules.AreHostile(a, b));
            Assert.IsTrue(FactionRules.AreHostile(b, a));
        }

        [Test]
        public void ADuelIsBetweenTheTwoDuelistsOnly()
        {
            FactionRules.CrossFactionPvp = true;
            Member a = Of(FactionId.Null), b = Of(FactionId.Null), bystander = Of(FactionId.Null);
            Member.Duel(a, b);
            Assert.IsFalse(FactionRules.AreHostile(a, bystander));
            Assert.IsFalse(FactionRules.AreHostile(bystander, b));
        }

        [Test]
        public void ADuelWorksWhileFactionPvpIsOff()
        {
            FactionRules.CrossFactionPvp = false;
            Member a = Of(FactionId.Hegemony), b = Of(FactionId.Covenant);
            Member.Duel(a, b);
            Assert.IsTrue(FactionRules.AreHostile(a, b));
        }

        [Test]
        public void OnNeutralGroundOtherFactionsAreNotHostile()
        {
            FactionRules.CrossFactionPvp = true;
            Member a = Of(FactionId.Hegemony), b = Of(FactionId.Null);
            Assert.IsFalse(FactionRules.AreHostile(a, b, neutralGround: true));
            Assert.IsFalse(FactionRules.AreHostile(b, a, neutralGround: true));
        }

        [Test]
        public void OnNeutralGroundADuelIsStillAFight_BetweenTheTwoOnly()
        {
            FactionRules.CrossFactionPvp = true;
            Member a = Of(FactionId.Hegemony), b = Of(FactionId.Null), bystander = Of(FactionId.Covenant);
            Member.Duel(a, b);
            Assert.IsTrue(FactionRules.AreHostile(a, b, neutralGround: true));
            Assert.IsTrue(FactionRules.AreHostile(b, a, neutralGround: true));
            Assert.IsFalse(FactionRules.AreHostile(a, bystander, neutralGround: true));
        }

        [Test]
        public void OffNeutralGroundNothingChanges()
        {
            Member a = Of(FactionId.Hegemony), b = Of(FactionId.Null), mate = Of(FactionId.Hegemony);
            foreach (bool pvp in new[] { true, false })
            {
                FactionRules.CrossFactionPvp = pvp;
                Assert.AreEqual(FactionRules.AreHostile(a, b), FactionRules.AreHostile(a, b, neutralGround: false));
                Assert.AreEqual(FactionRules.AreHostile(a, mate), FactionRules.AreHostile(a, mate, neutralGround: false));
                Assert.IsFalse(FactionRules.AreHostile(a, null, neutralGround: false));
            }
        }

        [Test]
        public void AMemberIsNeverItsOwnEnemy()
        {
            FactionRules.CrossFactionPvp = true;
            Member a = Of(FactionId.Null);
            Assert.IsFalse(FactionRules.AreHostile(a, a));
            Assert.IsFalse(FactionRules.AreHostile(a, null));
        }

        [Test]
        public void TheCatalogDefaultIsTakenOnlyOnce()
        {
            // Configure applies once per session; a later call (another player loading) must not undo a GM switch.
            FactionRules.Configure(true);
            bool afterFirst = FactionRules.CrossFactionPvp;
            FactionRules.CrossFactionPvp = !afterFirst;
            FactionRules.Configure(afterFirst);
            Assert.AreEqual(!afterFirst, FactionRules.CrossFactionPvp);
        }
    }
}
