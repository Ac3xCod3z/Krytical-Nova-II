using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Wallet;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Build 17, item 2.4 (owner, 13 Nov 2026): a brand-new character starts with 20 HP Stims (S), 20 Flux Stims (S) and 2,500 Creds.
    /// The Stims come in the catalog's starter bag (a saved character's Pack replaces it, so an old one never gets them again); the Creds
    /// come once through the wallet when the save agent finds no save (<see cref="INewCharacterGrant"/>, <see cref="CredsSource.StarterKit"/>).
    /// </summary>
    public sealed class StarterKitTests
    {
        private const string CatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/ItemCatalog.asset";
        private const string EconomyPath = "Assets/_Game/Gameplay/Economy/Data/Assets/EconomySettings.asset";

        private static int InStarterBag(ItemCatalog catalog, ushort itemId)
        {
            int count = 0;
            foreach (var entry in catalog.StarterBag)
                if (entry.item != null && entry.item.itemId == itemId) count += entry.count;
            return count;
        }

        [Test]
        public void TheStarterBagHoldsTwentyOfEachSmallStim()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, CatalogPath);
            Assert.AreEqual(20, InStarterBag(catalog, 9001), "HP Stim (S)");
            Assert.AreEqual(20, InStarterBag(catalog, 9002), "Flux Stim (S)");
            Assert.AreEqual("HP Stim (S)", catalog.Get(9001).displayName);
            Assert.AreEqual("Flux Stim (S)", catalog.Get(9002).displayName);
        }

        [Test]
        public void ANewCharacterStartsWith2500Creds()
        {
            var economy = AssetDatabase.LoadAssetAtPath<EconomySettings>(EconomyPath);
            Assert.IsNotNull(economy, EconomyPath);
            Assert.AreEqual(2500, PlayerShopping.StartingCreds(economy));
            Assert.AreEqual(0, PlayerShopping.StartingCreds(null));
        }

        [Test]
        public void TheShopIsTheOneGrantAndItsSourceIsAppended()
        {
            Assert.IsTrue(typeof(INewCharacterGrant).IsAssignableFrom(typeof(PlayerShopping)));
            Assert.AreEqual(8, (int)CredsSource.StarterKit, "CredsSource is append-only");
        }
    }
}
