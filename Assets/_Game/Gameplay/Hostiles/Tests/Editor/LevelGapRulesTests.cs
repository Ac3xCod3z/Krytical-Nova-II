using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>The level-gap table of the Hostiles plan (10.1), gap by gap, on the settings' default values.</summary>
    public sealed class LevelGapRulesTests
    {
        private LevelGapSettings settings;

        [SetUp]
        public void SetUp() => settings = ScriptableObject.CreateInstance<LevelGapSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        private static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

        // gap, XP %, drop %, gear, hit modifier
        [TestCase(-12, 0f, 0f, false, 0f)]
        [TestCase(-11, 0f, 0f, false, 0f)]
        [TestCase(-10, 0f, 0f, false, 0f)]
        [TestCase(-9, 8f, 25f, false, 0f)]
        [TestCase(-8, 13f, 25f, false, 0f)]
        [TestCase(-7, 22f, 25f, false, 0f)]
        [TestCase(-6, 37f, 25f, false, 0f)]
        [TestCase(-5, 61f, 75f, true, 0f)]
        [TestCase(-4, 80f, 75f, true, 0f)]
        [TestCase(-3, 97f, 75f, true, 0f)]
        [TestCase(-2, 100f, 100f, true, 0f)]
        [TestCase(-1, 100f, 100f, true, 0f)]
        [TestCase(0, 100f, 100f, true, 0f)]
        [TestCase(1, 100f, 100f, true, 0f)]
        [TestCase(2, 100f, 100f, true, 0f)]
        [TestCase(3, 100f, 100f, true, 0f)]
        [TestCase(4, 100f, 100f, true, 0f)]
        [TestCase(5, 125f, 100f, true, -5f)]
        [TestCase(6, 135f, 100f, true, -5f)]
        [TestCase(7, 145f, 100f, true, -5f)]
        [TestCase(8, 155f, 100f, true, -5f)]
        [TestCase(9, 165f, 100f, true, -5f)]
        [TestCase(10, 175f, 100f, true, -15f)]
        [TestCase(11, 175f, 100f, true, -15f)]
        [TestCase(12, 175f, 100f, true, -15f)]
        public void EveryGapPaysWhatTheTableSays(int gap, float experience, float drops, bool gear, float hit)
        {
            Assert.AreEqual(experience, LevelGapRules.ExperiencePercent(settings, gap), 0.001f, "XP");
            Assert.AreEqual(drops, LevelGapRules.DropPercent(settings, gap), 0.001f, "drops");
            Assert.AreEqual(gear, LevelGapRules.GearDrops(settings, gap), "gear");
            Assert.AreEqual(hit, LevelGapRules.HitChanceModifier(settings, gap), 0.001f, "hit chance");
        }

        [Test]
        public void NamesTakeTheSixColours()
        {
            Assert.AreEqual(Hex(0x92, 0xA2, 0xB2), LevelGapRules.NameColor(settings, -10), "grey");
            Assert.AreEqual(Hex(0x6E, 0xAA, 0xFF), LevelGapRules.NameColor(settings, -6), "blue");
            Assert.AreEqual(Hex(0x72, 0xDE, 0xC4), LevelGapRules.NameColor(settings, -3), "teal");
            Assert.AreEqual(Hex(0xE6, 0xED, 0xF5), LevelGapRules.NameColor(settings, 0), "white");
            Assert.AreEqual(Hex(0xC8, 0x80, 0xFF), LevelGapRules.NameColor(settings, 5), "purple");
            Assert.AreEqual(Hex(0xFF, 0x5C, 0x52), LevelGapRules.NameColor(settings, 10), "red");
        }

        [Test]
        public void ExperienceNeverPassesTheCeilingAndFarGapsUseTheOuterBands()
        {
            for (int gap = -150; gap <= 150; gap++)
            {
                float percent = LevelGapRules.ExperiencePercent(settings, gap);
                Assert.LessOrEqual(percent, 175f, $"gap {gap}");
                if (gap <= -10) Assert.AreEqual(0f, percent, $"gap {gap}");
            }
            Assert.AreEqual(175f, LevelGapRules.ExperiencePercent(settings, 500));
            Assert.AreEqual(0f, LevelGapRules.ExperiencePercent(settings, -500));
        }

        [Test]
        public void ChangingAColourInTheSettingsChangesIt()
        {
            settings.bands[3].nameColor = Color.magenta;
            Assert.AreEqual(Color.magenta, LevelGapRules.NameColor(settings, 0));
        }

        [Test]
        public void AMemberFarBelowTheSquadsHighestIsPaidByTheHighestMembersGap()
        {
            // A level 10 Hostile: the level 20 member sees gap -10, the level 9 member +1.
            Assert.AreEqual(-10, LevelGapRules.SquadGap(settings, 1, -10), "11 levels below the highest: carried");
            Assert.AreEqual(-4, LevelGapRules.SquadGap(settings, 2, -4), "exactly 6 below: carried");
            Assert.AreEqual(1, LevelGapRules.SquadGap(settings, 1, -4), "5 below: paid by their own gap");
            Assert.AreEqual(-3, LevelGapRules.SquadGap(settings, -3, -3), "the highest member");
        }

        [Test]
        public void ADeathCostsFivePercentOfTheLevelAndNeverALevel()
        {
            Assert.AreEqual(50, LevelGapRules.DeathLoss(settings, 1000, 600));
            Assert.AreEqual(20, LevelGapRules.DeathLoss(settings, 1000, 20), "no more than the player holds");
            Assert.AreEqual(0, LevelGapRules.DeathLoss(settings, 1000, 0));
            Assert.AreEqual(0, LevelGapRules.DeathLoss(settings, 0, 0), "at the level cap");
        }

        [Test]
        public void KillExperienceFollowsGapSquadBoostAndNeverRoundsAPayingKillToNothing()
        {
            var stats = new HostileStats { Experience = 40 };
            Assert.AreEqual(40, HostileKillRewards.ExperienceFor(stats, settings, 0, 0f));
            Assert.AreEqual(70, HostileKillRewards.ExperienceFor(stats, settings, 10, 0f));
            Assert.AreEqual(48, HostileKillRewards.ExperienceFor(stats, settings, 0, 0.2f), "a full Squad together");
            Assert.AreEqual(0, HostileKillRewards.ExperienceFor(stats, settings, -10, 0.2f), "grey pays nothing, Squad or not");
            Assert.AreEqual(1, HostileKillRewards.ExperienceFor(new HostileStats { Experience = 2 }, settings, -9, 0f), "8% of 2");
        }
    }
}
