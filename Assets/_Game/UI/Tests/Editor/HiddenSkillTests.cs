using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, item 12.1: only learned (and level-1) skills show their name and icon in the skill tree.</summary>
    public sealed class HiddenSkillTests
    {
        [Test]
        public void AnUnlearnedSkillAboveLevelOneIsHidden()
        {
            Assert.IsTrue(ProgressionHUD.IsHiddenSkill(learned: false, requiredLevel: 30, canLearn: false));
            Assert.IsTrue(ProgressionHUD.IsHiddenSkill(learned: false, requiredLevel: 2, canLearn: false));
        }

        [Test]
        public void LearnedOrLevelOneSkillsShow()
        {
            Assert.IsFalse(ProgressionHUD.IsHiddenSkill(learned: true, requiredLevel: 30, canLearn: false));
            Assert.IsFalse(ProgressionHUD.IsHiddenSkill(learned: false, requiredLevel: 1, canLearn: false));
            Assert.IsFalse(ProgressionHUD.IsHiddenSkill(learned: false, requiredLevel: 0, canLearn: false));
        }

        [Test]
        public void ASkillThePlayerCanLearnNowShowsSoItsLearnButtonIsThere()
        {
            Assert.IsFalse(ProgressionHUD.IsHiddenSkill(learned: false, requiredLevel: 30, canLearn: true));
        }

        [Test]
        public void TheHiddenTileSaysOnlyTheLevel()
        {
            Assert.AreEqual("???", ProgressionHUD.HiddenSkillName);
            StringAssert.Contains("level 30", ProgressionHUD.HiddenSkillTooltip(30));
        }
    }
}
