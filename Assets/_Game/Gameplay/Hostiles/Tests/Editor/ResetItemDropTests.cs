using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>
    /// The Respec Chip (9037) and the Memory Wipe (9038) fall from a Hostile of any level and are sold at the Apothecary (wave 3b,
    /// owner 12 Nov 2026). The chance and the price are the assets' to tune: the tests pin that the row and the listing exist, not
    /// their numbers, and that a grey level gap pays nothing as for every drop.
    /// </summary>
    public sealed class ResetItemDropTests
    {
        private const string SettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileSettings.asset";
        private const string ApothecaryPath = "Assets/_Game/Gameplay/Economy/Data/Assets/Vendors/VS_Apothecary.asset";
        private static readonly ushort[] Ids = { 9037, 9038 };

        [Test]
        public void AnAnyLevelRow_DropsAtEveryLevel_AndNothingAtAGreyGap()
        {
            var settings = ScriptableObject.CreateInstance<HostileSettings>();
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            settings.materialDrops = new[] { new MaterialDrop { item = item, chancePercent = 0.05f } };
            var results = new List<ItemDefinition>();
            foreach (int level in new[] { 1, 40, 150 })
            {
                HostileLoot.RollMaterials(level, settings, 100f, 1f, () => 0.0004f, results);
                Assert.AreEqual(1, results.Count, $"level {level}");
            }
            HostileLoot.RollMaterials(40, settings, 0f, 1f, () => 0f, results);
            Assert.AreEqual(0, results.Count, "a grey Hostile drops nothing");
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(item);
        }

        [Test]
        public void BothItems_AreOnTheDropTable_AtAnyLevel()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HostileSettings>(SettingsPath);
            Assert.IsNotNull(settings);
            foreach (ushort id in Ids)
            {
                MaterialDrop row = System.Array.Find(settings.materialDrops, drop => drop.item != null && drop.item.itemId == id);
                Assert.IsNotNull(row.item, $"item {id} has a drop row (Krytical Nova > Items > 5. Generate Utility Items)");
                Assert.AreEqual(0, row.minLevel, $"{id}: any level");
                Assert.AreEqual(0, row.maxLevel, $"{id}: any level");
                Assert.Greater(row.chancePercent, 0f);
            }
        }

        [Test]
        public void BothItems_AreSoldAtTheApothecary()
        {
            var stock = AssetDatabase.LoadAssetAtPath<VendorStock>(ApothecaryPath);
            Assert.IsNotNull(stock);
            foreach (ushort id in Ids)
            {
                VendorListing listing = stock.listings.Find(l => l.item != null && l.item.itemId == id);
                Assert.IsNotNull(listing.item, $"item {id} is listed (Krytical Nova > Economy > vendor stock builder)");
                Assert.Greater(listing.price, 0L);
            }
        }
    }
}
