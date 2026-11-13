using KryticalNova.Progression;
using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.UI.Tests
{
    /// <summary>Build 17 item 15.1: a mount's evolution shows the milestone banner with its stage and both stats.</summary>
    public sealed class MountBannerTests
    {
        private MountDefinition mount;

        [SetUp]
        public void SetUp()
        {
            mount = ScriptableObject.CreateInstance<MountDefinition>();
            mount.displayName = "Brambleback";
            mount.firstStat = StatType.Vitality;
            mount.firstMax = 50f;
            mount.secondStat = StatType.Strength;
            mount.secondMax = 50f;
        }

        // what PlayerMounts.StatLine reads, without the NGO behaviour in this assembly's references
        private static string Line(StatType stat, int growth) =>
            StatDisplay.FormatModifier(stat, ModifierType.Flat, MountRules.StatBonus(50f, growth, true));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(mount);

        [Test]
        public void TheBannerNamesTheStageAndBothStats()
        {
            string detail = PetHudController.MountBannerDetail(mount, 1);
            StringAssert.StartsWith(mount.StageName(1), detail);
            StringAssert.Contains(Line(StatType.Vitality, MountRules.StageStart(1)), detail);
            StringAssert.Contains(Line(StatType.Strength, MountRules.StageStart(1)), detail);
        }

        [Test]
        public void TheLastStageShowsTheFullStats()
        {
            string full = Line(StatType.Vitality, MountRules.GrowthSteps);
            StringAssert.Contains(full, PetHudController.MountBannerDetail(mount, MountRules.LastStage));
        }
    }
}
