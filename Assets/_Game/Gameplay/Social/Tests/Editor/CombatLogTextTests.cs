using KryticalNova.Core;
using KryticalNova.Social;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, item 4.1 (owner, 13 Nov 2026): a hit's elemental part shows in brackets in the Combat log.</summary>
    public sealed class CombatLogTextTests
    {
        [Test]
        public void YourHitWithAnElementalPartSaysItInBrackets()
        {
            Assert.AreEqual("You hit Bob for 1,200 (+340 ele dmg).", PlayerChat.CombatLineText(true, "Bob", 1200f, 0f, 0, 340f));
            Assert.AreEqual("You critically hit Bob for 2,400 (+340 ele dmg).", PlayerChat.CombatLineText(true, "Bob", 2400f, 0f, 1, 340f));
        }

        [Test]
        public void AHitOnYouWithAnElementalPartSaysItToo()
        {
            Assert.AreEqual("Rat hits you for 90 (+12 ele dmg).", PlayerChat.CombatLineText(false, "Rat", 90f, 0f, 0, 12f));
            Assert.AreEqual("Rat critically hits you for 180 (+12 ele dmg).", PlayerChat.CombatLineText(false, "Rat", 180f, 0f, 1, 12f));
        }

        [Test]
        public void NoElementalPartNoBracket()
        {
            Assert.AreEqual("You hit Bob for 1,200.", PlayerChat.CombatLineText(true, "Bob", 1200f, 0f, 0, 0f));
            Assert.AreEqual("You hit Bob for 50.", PlayerChat.CombatLineText(true, "Bob", 50f, 0f, 0, 0.3f), "under 1 rounds away");
            Assert.AreEqual("Your attack missed Bob.", PlayerChat.CombatLineText(true, "Bob", 0f, 0f, 2, 0f));
        }

        [Test]
        public void AMissOrAReflectedBlowCarriesNoElementalPart()
        {
            Assert.AreEqual(0f, PlayerChat.ElementalPart(new DamageResult { IsEvaded = true, NetElementalDamage = 40f }));
            Assert.AreEqual(0f, PlayerChat.ElementalPart(new DamageResult { IsReflected = true, NetElementalDamage = 40f }));
            Assert.AreEqual(40f, PlayerChat.ElementalPart(new DamageResult { DamageDealt = 140f, NetElementalDamage = 40f }));
        }
    }
}
