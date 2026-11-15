using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Mount Companion phase 5 (owner, 14 Nov 2026): a Hostile kill the companion helped with in the last 10 s (a DPS strike, a TANK taunt,
    /// a HEALER pulse in that fight) gives the selected mount 1.5x its kill EXP; it multiplies with the Rider Booster (1.5 x 2 = 3); FREE
    /// never helps; help older than 10 s, or with another Hostile, gives nothing extra.
    /// </summary>
    public sealed class CompanionHelpedKillTests
    {
        const string SettingsPath = "Assets/_Game/Gameplay/Mounts/Data/Assets/MountSettings.asset";
        const float Multiplier = 1.5f, Window = 10f;
        static readonly MountTuning Tuning = MountTuning.Default;   // a kill is 4 units

        static uint UnitsFor(CompanionHelpLog log, object hostile, float killAt, float boosterBonus)
        {
            bool helped = log.TakeHelped(hostile, killAt, Window);
            return MountRules.KillXpUnits(Tuning, boosterBonus, CompanionHelpRules.KillMultiplier(helped, Multiplier));
        }

        [TestCase(CompanionStance.Dps)]
        [TestCase(CompanionStance.Healer)]
        [TestCase(CompanionStance.Tank)]
        public void EachCombatStance_Helps_OneAndAHalfTimes(CompanionStance stance)
        {
            var log = new CompanionHelpLog();
            var hostile = new object();
            log.Note(hostile, 100f, stance);
            Assert.AreEqual(6u, UnitsFor(log, hostile, 104f, 0f), $"{stance}: 4 units x 1.5");
        }

        [Test]
        public void FreeStance_GivesNoBonus()
        {
            var log = new CompanionHelpLog();
            var hostile = new object();
            log.Note(hostile, 100f, CompanionStance.Free);
            Assert.IsFalse(CompanionHelpRules.Counts(CompanionStance.Free));
            Assert.AreEqual(4u, UnitsFor(log, hostile, 101f, 0f));
        }

        [Test]
        public void NoBonus_After10Seconds()
        {
            var log = new CompanionHelpLog();
            var hostile = new object();
            log.Note(hostile, 100f, CompanionStance.Dps);
            Assert.AreEqual(4u, UnitsFor(log, hostile, 110.5f, 0f), "help 10.5 s before the kill");
            log.Note(hostile, 200f, CompanionStance.Dps);
            Assert.AreEqual(6u, UnitsFor(log, hostile, 210f, 0f), "exactly 10 s still counts");
        }

        [Test]
        public void Stacks_ByMultiplying_WithTheRiderBooster()
        {
            var log = new CompanionHelpLog();
            var hostile = new object();
            log.Note(hostile, 100f, CompanionStance.Tank);
            Assert.AreEqual(12u, UnitsFor(log, hostile, 105f, 1f), "4 x 1.5 x 2 = 3x");
            Assert.AreEqual(8u, UnitsFor(log, hostile, 105f, 1f), "the note is used up: Rider alone");
        }

        [Test]
        public void HelpWithAnotherHostile_DoesNotCount()
        {
            var log = new CompanionHelpLog();
            var helped = new object();
            var other = new object();
            log.Note(helped, 100f, CompanionStance.Dps);
            Assert.AreEqual(4u, UnitsFor(log, other, 101f, 0f));
            Assert.AreEqual(6u, UnitsFor(log, helped, 101f, 0f));
        }

        [Test]
        public void TheLog_KeepsTheNewestWhenFull()
        {
            var log = new CompanionHelpLog();
            var hostiles = new object[CompanionHelpLog.Capacity + 1];
            for (int i = 0; i < hostiles.Length; i++) { hostiles[i] = new object(); log.Note(hostiles[i], 100f + i * 0.1f, CompanionStance.Tank); }
            Assert.IsFalse(log.TakeHelped(hostiles[0], 103f, Window), "the oldest gave way");
            Assert.IsTrue(log.TakeHelped(hostiles[hostiles.Length - 1], 103f, Window));
        }

        [Test]
        public void TheSettingsAsset_Holds_OneAndAHalf_And10Seconds()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MountSettings>(SettingsPath);
            Assert.IsNotNull(settings, SettingsPath);
            Assert.AreEqual(1.5f, settings.companionHelpedKillXpMultiplier);
            Assert.AreEqual(10f, settings.companionHelpSeconds);
        }
    }
}
