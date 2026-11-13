using KryticalNova.UI;
using NUnit.Framework;

namespace KryticalNova.UI.Tests
{
    /// <summary>Build 17 item 2.5: the own level-up banner names the level reached.</summary>
    public sealed class LevelUpBannerTests
    {
        [TestCase(2, "LEVEL 2")]
        [TestCase(37, "LEVEL 37")]
        [TestCase(112, "LEVEL 112")]
        public void TheBannerNamesTheLevel(int level, string title) => Assert.AreEqual(title, HeraldWindowController.LevelBannerTitle(level));
    }
}
