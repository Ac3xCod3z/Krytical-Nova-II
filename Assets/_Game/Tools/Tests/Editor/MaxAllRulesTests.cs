using System;
using System.Collections.Generic;
using KryticalNova.DevTools;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// gm.maxall (owner's night list item 22): it runs only on the test database, gives the caller's own faction / lineage / class line of
    /// Rare and Epic gear (Rare clean, Epic at the Temper cap and Fusion 12), places one-cell pieces Pack -> Vault -> Deep Vault in order,
    /// and puts pets at 100 % growth, fed.
    /// </summary>
    public class MaxAllRulesTests
    {
        static readonly int[] RealPorts = { 7777, 7778, 7801, 8090 };

        // ------------------------------------------------------------------ the gate

        [Test]
        public void Gate_TestSaveService_Runs()
        {
            Assert.IsNull(MaxAllRules.GateProblem(false, new Uri("http://127.0.0.1:7802/"), RealPorts));
            Assert.IsNull(MaxAllRules.GateProblem(true, new Uri("http://127.0.0.1:7802/"), RealPorts));
        }

        [Test]
        public void Gate_RealSaveService_Refused_EvenInPlayDotsHere()
        {
            StringAssert.Contains("REAL", MaxAllRules.GateProblem(false, new Uri("http://127.0.0.1:7801/"), RealPorts));
            StringAssert.Contains("REAL", MaxAllRules.GateProblem(true, new Uri("http://127.0.0.1:7801/"), RealPorts));
        }

        [Test]
        public void Gate_OtherSaveService_Refused()
        {
            Assert.IsNotNull(MaxAllRules.GateProblem(false, new Uri("http://127.0.0.1:9000/"), RealPorts));
        }

        [Test]
        public void Gate_Files_RunsOnlyInPlayDotsHere()
        {
            Assert.IsNull(MaxAllRules.GateProblem(true, null, RealPorts));
            Assert.IsNotNull(MaxAllRules.GateProblem(false, null, RealPorts), "a built server (or any other session) saving to files is not the test database");
        }

        [Test]
        public void Confirm_NeedsTheWord()
        {
            Assert.IsTrue(MaxAllRules.IsConfirm("confirm"));
            Assert.IsTrue(MaxAllRules.IsConfirm(" CONFIRM "));
            Assert.IsFalse(MaxAllRules.IsConfirm(""));
            Assert.IsFalse(MaxAllRules.IsConfirm(null));
            Assert.IsFalse(MaxAllRules.IsConfirm("yes"));
        }

        // ------------------------------------------------------------------ which gear

        static ushort s_NextId;

        static GearDictionary.Entry Entry(EquipSlot slot, ItemRarity rarity, byte level, FactionMask factions = FactionMask.Hegemony,
            BodyFit bodies = BodyFit.Humanoid, string pack = "SCFI_SOLD", WeaponClassId cls = WeaponClassId.None) =>
            new GearDictionary.Entry
            {
                itemId = (ushort)(10000 + ++s_NextId), slot = slot, rarity = rarity, itemLevel = level, factions = factions, bodies = bodies,
                pack = slot == EquipSlot.Weapon || slot == EquipSlot.Ring || slot == EquipSlot.Amulet ? string.Empty : pack, weaponClass = cls,
            };

        static List<GearDictionary.Entry> Pick(List<GearDictionary.Entry> entries) =>
            MaxAllRules.PickGear(entries, FactionId.Hegemony, BodyFit.Humanoid, "SCFI_SOLD", WeaponClassId.Monoblade);

        [Test]
        public void Gear_OnlyRareAndEpic()
        {
            var entries = new List<GearDictionary.Entry>
            {
                Entry(EquipSlot.Armor, ItemRarity.Common, 10), Entry(EquipSlot.Armor, ItemRarity.Uncommon, 10),
                Entry(EquipSlot.Armor, ItemRarity.Rare, 10), Entry(EquipSlot.Armor, ItemRarity.Epic, 10), Entry(EquipSlot.Armor, ItemRarity.Legendary, 10),
            };
            List<GearDictionary.Entry> picked = Pick(entries);
            Assert.AreEqual(2, picked.Count);
            Assert.AreEqual(ItemRarity.Rare, picked[0].rarity);
            Assert.AreEqual(ItemRarity.Epic, picked[1].rarity);
        }

        [Test]
        public void Gear_EveryLevelOfEverySlot_OnePieceEach()
        {
            var entries = new List<GearDictionary.Entry>();
            foreach (EquipSlot slot in new[] { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape })
                foreach (byte level in new byte[] { 30, 60, 90 })
                {
                    entries.Add(Entry(slot, ItemRarity.Rare, level));
                    entries.Add(Entry(slot, ItemRarity.Rare, level)); // a second look of the same tier: not given twice
                    entries.Add(Entry(slot, ItemRarity.Epic, level));
                }
            Assert.AreEqual(6 * 3 * 2, Pick(entries).Count);
        }

        [Test]
        public void Gear_OwnFactionBodyAndLineageOnly()
        {
            GearDictionary.Entry mine = Entry(EquipSlot.Armor, ItemRarity.Epic, 50);
            var entries = new List<GearDictionary.Entry>
            {
                Entry(EquipSlot.Armor, ItemRarity.Epic, 50, factions: FactionMask.Null),
                Entry(EquipSlot.Armor, ItemRarity.Epic, 50, bodies: BodyFit.Robot),
                Entry(EquipSlot.Armor, ItemRarity.Epic, 50, pack: "SCFI_ROBO"),
                mine,
                Entry(EquipSlot.Pet, ItemRarity.Epic, 50),
            };
            List<GearDictionary.Entry> picked = Pick(entries);
            Assert.AreEqual(1, picked.Count);
            Assert.AreEqual(mine.itemId, picked[0].itemId);
        }

        [Test]
        public void Gear_WeaponsOfTheClassLineOnly()
        {
            var entries = new List<GearDictionary.Entry>
            {
                Entry(EquipSlot.Weapon, ItemRarity.Rare, 40, cls: WeaponClassId.Glaive),
                Entry(EquipSlot.Weapon, ItemRarity.Rare, 40, cls: WeaponClassId.Monoblade),
                Entry(EquipSlot.Weapon, ItemRarity.Epic, 40, cls: WeaponClassId.Carbine),
                Entry(EquipSlot.Weapon, ItemRarity.Epic, 40, cls: WeaponClassId.Monoblade),
            };
            List<GearDictionary.Entry> picked = Pick(entries);
            Assert.AreEqual(2, picked.Count);
            Assert.IsTrue(picked.TrueForAll(e => e.weaponClass == WeaponClassId.Monoblade));
        }

        [Test]
        public void Gear_RareClean_EpicTemperCapFusion12()
        {
            Assert.AreEqual(0UL, MaxAllRules.GearState(ItemRarity.Rare, 120));
            GearItemState epic = GearItemState.Unpack(MaxAllRules.GearState(ItemRarity.Epic, 120));
            Assert.AreEqual(120, epic.Temper);
            Assert.AreEqual(12, epic.Fusion);
            Assert.AreEqual(0, epic.Tune);
        }

        // ------------------------------------------------------------------ where it goes

        [Test]
        public void Fill_EmptyCellsOnly_InOrder_RestKeepsWaiting()
        {
            var cells = new ItemStack[4];
            cells[1] = new ItemStack(9001, 5);
            var waiting = new List<ItemStack> { new ItemStack(11, 1), new ItemStack(12, 1), new ItemStack(13, 1), new ItemStack(14, 1) };
            var placed = new List<ItemStack>();
            Assert.AreEqual(3, MaxAllRules.FillEmpty(cells, waiting, placed));
            Assert.AreEqual(11, cells[0].itemId);
            Assert.AreEqual(9001, cells[1].itemId, "a used cell is never overwritten");
            Assert.AreEqual(12, cells[2].itemId);
            Assert.AreEqual(13, cells[3].itemId);
            Assert.AreEqual(3, placed.Count);
            Assert.AreEqual(1, waiting.Count);
            Assert.AreEqual(14, waiting[0].itemId, "what found no room waits for the next storage");
        }

        [Test]
        public void Fill_PackThenVaultThenDeepVault_LeavesTheDidNotFitList()
        {
            var pack = new ItemStack[2];
            var vault = new ItemStack[2];
            var deep = new ItemStack[1];
            var waiting = new List<ItemStack>();
            for (ushort id = 1; id <= 7; id++) waiting.Add(new ItemStack(id, 1));
            Assert.AreEqual(2, MaxAllRules.FillEmpty(pack, waiting, null));
            Assert.AreEqual(2, MaxAllRules.FillEmpty(vault, waiting, null));
            Assert.AreEqual(1, MaxAllRules.FillEmpty(deep, waiting, null));
            Assert.AreEqual(new ushort[] { 6, 7 }, waiting.ConvertAll(s => s.itemId).ToArray());
            StringAssert.Contains("2:", MaxAllRules.DidNotFit(new[] { "Blade", "Blade" }));
            StringAssert.Contains("Blade x2", MaxAllRules.DidNotFit(new[] { "Blade", "Blade" }));
            StringAssert.Contains("nothing", MaxAllRules.DidNotFit(new string[0]));
        }

        // ------------------------------------------------------------------ pets

        [Test]
        public void Pet_FullGrowthAndFed()
        {
            PetTuning tuning = PetTuning.Default;
            PetItemState hungry = new PetItemState(0, PetItemState.FullHungerUnits / 2);
            PetItemState full = PetItemState.Unpack(MaxAllRules.FullPet(hungry.Pack(), tuning));
            Assert.AreEqual(100, PetRules.GrowthPercent(full, tuning));
            Assert.AreEqual(100f, full.HungerPercent, 0.001f);
        }

        [Test]
        public void Pet_NeverLosesXpItAlreadyHas()
        {
            PetTuning tuning = PetTuning.Default;
            var over = new PetItemState(uint.MaxValue / 2, 0);
            Assert.AreEqual(over.XpUnits, PetItemState.Unpack(MaxAllRules.FullPet(over.Pack(), tuning)).XpUnits);
        }
    }
}
