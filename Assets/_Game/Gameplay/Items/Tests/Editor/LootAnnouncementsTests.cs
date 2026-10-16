using NUnit.Framework;

namespace KryticalNova.Items.Tests
{
    /// <summary>Which finds are news for the whole server, and what the line says.</summary>
    public sealed class LootAnnouncementsTests
    {
        [Test]
        public void OnlyEpicAndAboveIsAnnounced_WhenTheLowestIsEpic()
        {
            Assert.IsFalse(LootAnnouncements.IsAnnounced(true, ItemRarity.Epic, ItemRarity.Common));
            Assert.IsFalse(LootAnnouncements.IsAnnounced(true, ItemRarity.Epic, ItemRarity.Rare));
            Assert.IsTrue(LootAnnouncements.IsAnnounced(true, ItemRarity.Epic, ItemRarity.Epic));
            Assert.IsTrue(LootAnnouncements.IsAnnounced(true, ItemRarity.Epic, ItemRarity.Legendary));
        }

        [Test]
        public void SwitchedOffAnnouncesNothing()
        {
            Assert.IsFalse(LootAnnouncements.IsAnnounced(false, ItemRarity.Common, ItemRarity.Legendary));
        }

        [Test]
        public void TheLineNamesTheFinderAndTheItem()
        {
            Assert.AreEqual("Arinis found Sunlord Blade.", LootAnnouncements.Text("Arinis", "Sunlord Blade"));
        }
    }
}
