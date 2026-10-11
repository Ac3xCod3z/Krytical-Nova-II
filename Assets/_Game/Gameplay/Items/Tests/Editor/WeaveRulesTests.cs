using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class WeaveRulesTests
    {
        private const string Root = "Assets/_Game/Gameplay/Items/Data/Assets/";
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ItemDefinition Piece(ushort id, EquipSlot slot, ushort line, WeaponClassId weaponClass = WeaponClassId.None, bool look = true)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.equipSlot = slot;
            item.lineId = line;
            item.weaponClass = weaponClass;
            if (look && slot == EquipSlot.Weapon)
            {
                item.weaponPrefab = new GameObject("weapon model");
                made.Add(item.weaponPrefab);
            }
            else if (look) item.appearancePartIds = new ushort[] { 1 };
            made.Add(item);
            return item;
        }

        private static string Blocked(EquipSlot slot, ItemDefinition worn, ItemDefinition weave, bool owned = true) =>
            WeaveRules.ApplyBlockedReason(null, slot, worn, weave, owned, FactionId.None, BodyFit.None);

        [Test]
        public void ALookIsCollectedByItsLine_WhateverTheTier()
        {
            ItemDefinition low = Piece(10016, EquipSlot.Armor, 2);
            ItemDefinition high = Piece(10019, EquipSlot.Armor, 2);
            Assert.AreEqual(2, WeaveRules.KeyOf(low));
            Assert.AreEqual(WeaveRules.KeyOf(low), WeaveRules.KeyOf(high));
            Assert.AreEqual(10016, WeaveRules.ItemOf(2));
            Assert.AreEqual(0, WeaveRules.ItemOf(0));
        }

        [Test]
        public void WhatHasNoLook_IsNotCollected()
        {
            Assert.AreEqual(0, WeaveRules.KeyOf(null));
            Assert.AreEqual(0, WeaveRules.KeyOf(Piece(10032, EquipSlot.Ring, 3)), "a Ring is stats only");
            Assert.AreEqual(0, WeaveRules.KeyOf(Piece(2001, EquipSlot.Armor, 0)), "a look template is not ladder gear");
            Assert.AreEqual(0, WeaveRules.KeyOf(Piece(10048, EquipSlot.Weapon, 4, WeaponClassId.None, look: false)));
        }

        [Test]
        public void AWeaveGoesOnItsOwnSlot_OverSomethingWorn_WhenOwned()
        {
            ItemDefinition worn = Piece(10016, EquipSlot.Armor, 2);
            ItemDefinition weave = Piece(10064, EquipSlot.Armor, 5);
            Assert.IsNull(Blocked(EquipSlot.Armor, worn, weave));
            Assert.AreEqual(WeaveRules.ReasonNotOwned, Blocked(EquipSlot.Armor, worn, weave, owned: false));
            Assert.AreEqual(WeaveRules.ReasonNothingWorn, Blocked(EquipSlot.Armor, null, weave));
            Assert.AreEqual(WeaveRules.ReasonWrongSlot, Blocked(EquipSlot.Boots, worn, weave));
            Assert.AreEqual(WeaveRules.ReasonNotAWeave, Blocked(EquipSlot.Ring, worn, weave));
            Assert.AreEqual(WeaveRules.ReasonNotAWeave, Blocked(EquipSlot.Armor, worn, null));
        }

        [Test]
        public void AWeaponWeave_StaysWithinItsWeaponClass()
        {
            ItemDefinition blade = Piece(10080, EquipSlot.Weapon, 6, (WeaponClassId)1);
            ItemDefinition otherBlade = Piece(10096, EquipSlot.Weapon, 7, (WeaponClassId)1);
            ItemDefinition bow = Piece(10112, EquipSlot.Weapon, 8, (WeaponClassId)2);
            Assert.IsNull(Blocked(EquipSlot.Weapon, blade, otherBlade));
            Assert.AreEqual(WeaveRules.ReasonWrongClass, Blocked(EquipSlot.Weapon, blade, bow));
        }

        [Test]
        public void TheSlotShowsTheWeave_UntilItNoLongerSuitsWhatIsWorn()
        {
            ItemDefinition blade = Piece(10080, EquipSlot.Weapon, 6, (WeaponClassId)1);
            ItemDefinition otherBlade = Piece(10096, EquipSlot.Weapon, 7, (WeaponClassId)1);
            ItemDefinition bow = Piece(10112, EquipSlot.Weapon, 8, (WeaponClassId)2);
            ItemDefinition armor = Piece(10016, EquipSlot.Armor, 2);

            Assert.AreSame(otherBlade, WeaveRules.Shown(blade, otherBlade));
            Assert.AreSame(blade, WeaveRules.Shown(blade, null), "no Weave: the weapon itself");
            Assert.AreSame(bow, WeaveRules.Shown(bow, otherBlade), "another class in the hand shows itself");
            Assert.IsNull(WeaveRules.Shown(null, otherBlade), "an empty slot shows nothing");
            Assert.AreSame(armor, WeaveRules.Shown(armor, otherBlade), "a Weave of another slot is ignored");
        }

        [Test]
        public void OnlyAnOutfitPieceThatPaintsTheColourMap_TakesDye()
        {
            ItemDefinition armor = Piece(10016, EquipSlot.Armor, 2);
            armor.colorCells = new byte[] { 3, 4 };
            ItemDefinition plain = Piece(10128, EquipSlot.Boots, 9);
            ItemDefinition blade = Piece(10080, EquipSlot.Weapon, 6, (WeaponClassId)1);

            Assert.IsNull(WeaveRules.DyeBlockedReason(EquipSlot.Armor, armor, 0, null), "taking the dye off needs no colourway");
            Assert.AreEqual(WeaveRules.ReasonNoColourway, WeaveRules.DyeBlockedReason(EquipSlot.Armor, armor, 7, null));
            Assert.AreEqual(WeaveRules.ReasonNoDye, WeaveRules.DyeBlockedReason(EquipSlot.Boots, plain, 0, null));
            Assert.AreEqual(WeaveRules.ReasonNoDye, WeaveRules.DyeBlockedReason(EquipSlot.Weapon, blade, 0, null));
            Assert.AreEqual(WeaveRules.ReasonNothingWorn, WeaveRules.DyeBlockedReason(EquipSlot.Armor, null, 0, null));
        }

        [Test]
        public void EveryGearLineWithALook_HasItsFirstItemInTheCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "ItemCatalog.asset");
            var gear = AssetDatabase.LoadAssetAtPath<GearDictionary>(Root + "GearDictionary.asset");
            Assert.IsNotNull(catalog, "ItemCatalog.asset");
            Assert.IsNotNull(gear, "GearDictionary.asset");

            int looks = 0;
            foreach (GearDictionary.Line line in gear.Lines)
            {
                if (!line.IsValid || !WeaveRules.IsWeaveSlot(line.slot)) continue;
                ItemDefinition first = catalog.Get(WeaveRules.ItemOf(line.lineId));
                Assert.IsNotNull(first, $"line {line.lineId} ({line.displayName}): item {WeaveRules.ItemOf(line.lineId)} is what its Weave shows");
                Assert.AreEqual(line.lineId, first.lineId, $"line {line.lineId}");
                if (WeaveRules.KeyOf(first) != 0) looks++;
            }
            Assert.Greater(looks, 0);
        }

        [Test]
        public void TheCatalogNamesTheColourways()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "ItemCatalog.asset");
            Assert.IsNotNull(catalog.Themes, "ItemCatalog.asset has no colourways: run Krytical Nova/Items/1. Generate Gear Looks From Synty");
            Assert.Greater(catalog.Themes.Themes.Count, 0);
        }
    }
}
