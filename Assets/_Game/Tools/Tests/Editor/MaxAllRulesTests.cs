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
    /// gm.maxall (owner's night list item 22): any GM may run it (with the word confirm, live or test server), it gives the caller's own faction / lineage / class line of
    /// Rare and Epic gear, only the top set of each bracket it can wear (owner 15 Nov 2026; Rare clean, Epic at the Temper cap and Fusion 12), places one-cell pieces Pack -> Vault -> Deep Vault in order,
    /// and puts pets at 100 % growth, fed.
    /// </summary>
    public class MaxAllRulesTests
    {
        // ------------------------------------------------------------------ who may run it (owner, 14 Nov 2026: any GM, live or test server)

        [Test]
        public void Gm_WithConfirm_Runs()
        {
            // nothing about the save path is asked: a GM on the real server's save service is allowed as on the test one
            Assert.IsNull(MaxAllRules.Refusal(true, "confirm"));
        }

        [Test]
        public void NonGm_Refused_EvenWithConfirm()
        {
            StringAssert.Contains("Only a GM", MaxAllRules.Refusal(false, "confirm"));
            StringAssert.Contains("Only a GM", MaxAllRules.Refusal(false, ""));
        }

        [Test]
        public void Gm_WithoutConfirm_IsAskedForIt()
        {
            StringAssert.Contains("gm.maxall confirm", MaxAllRules.Refusal(true, ""));
            StringAssert.Contains("gm.maxall confirm", MaxAllRules.Refusal(true, "yes"));
        }

        [Test]
        public void RecordLine_NamesTheGmTheTargetAndTheTotals()
        {
            string line = MaxAllRules.RecordLine("GM Ace", "Tester (acct 1234)", 110, 4, 2, 2_000_000, 0);
            StringAssert.Contains("GM Ace", line);
            StringAssert.Contains("Tester (acct 1234)", line);
            StringAssert.Contains("110 gear", line);
            StringAssert.Contains("4 pets", line);
            StringAssert.Contains("2 mounts", line);
            StringAssert.Contains("2,000,000 Creds", line);
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

        static List<GearDictionary.Entry> Pick(List<GearDictionary.Entry> entries, int level = 112, System.Func<ushort, int> bandOf = null) =>
            MaxAllRules.PickGear(entries, bandOf, level, FactionId.Hegemony, BodyFit.Humanoid, "SCFI_SOLD", WeaponClassId.Monoblade);

        // The real ladder's brackets (GearStatTable bands): Rare I, Rare II and Epic, each its own line per slot.
        const int RareI = 2, RareII = 3, EpicBand = 4;
        static readonly byte[] RareILevels = { 45, 55, 65, 75 }, RareIILevels = { 85, 95, 105, 110 }, EpicLevels = { 100, 103, 106, 109, 112 };
        static readonly EquipSlot[] LadderSlots =
            { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape };

        /// <summary>Every tier of every bracket for every slot (weapons of the caller's class), and which band each line is.</summary>
        static List<GearDictionary.Entry> Ladder(Dictionary<ushort, int> bands)
        {
            var entries = new List<GearDictionary.Entry>();
            ushort lineId = 0;
            foreach (EquipSlot slot in LadderSlots)
                foreach ((int band, ItemRarity rarity, byte[] levels) in new[]
                         { (RareI, ItemRarity.Rare, RareILevels), (RareII, ItemRarity.Rare, RareIILevels), (EpicBand, ItemRarity.Epic, EpicLevels) })
                {
                    bands[++lineId] = band;
                    foreach (byte level in levels)
                    {
                        GearDictionary.Entry entry = Entry(slot, rarity, level, cls: WeaponClassId.Monoblade);
                        entry.lineId = lineId;
                        entries.Add(entry);
                    }
                }
            return entries;
        }

        /// <summary>The item level picked for each slot and bracket, as "Armor R75 R110 E112" (bracket order).</summary>
        static string Levels(List<GearDictionary.Entry> picked, EquipSlot slot)
        {
            var parts = new List<string> { slot.ToString() };
            foreach (GearDictionary.Entry e in picked)
                if (e.slot == slot) parts.Add((e.rarity == ItemRarity.Epic ? "E" : "R") + e.itemLevel);
            return string.Join(" ", parts);
        }

        static List<GearDictionary.Entry> PickLadderAt(int level)
        {
            var bands = new Dictionary<ushort, int>();
            List<GearDictionary.Entry> ladder = Ladder(bands);
            return Pick(ladder, level, id => bands.TryGetValue(id, out int b) ? b : -1);
        }

        [Test]
        public void TopSet_AtLevel112_Rare75_Rare110_Epic112_PerSlot()
        {
            List<GearDictionary.Entry> picked = PickLadderAt(112);
            Assert.AreEqual(LadderSlots.Length * 3, picked.Count);
            foreach (EquipSlot slot in LadderSlots) Assert.AreEqual(slot + " R75 R110 E112", Levels(picked, slot));
        }

        [Test]
        public void TopSet_AtLevel110_EpicIs109()
        {
            List<GearDictionary.Entry> picked = PickLadderAt(110);
            Assert.AreEqual(LadderSlots.Length * 3, picked.Count);
            foreach (EquipSlot slot in LadderSlots) Assert.AreEqual(slot + " R75 R110 E109", Levels(picked, slot));
        }

        [Test]
        public void TopSet_AtLevel75_OnlyRare75()
        {
            List<GearDictionary.Entry> picked = PickLadderAt(75);
            Assert.AreEqual(LadderSlots.Length, picked.Count);
            foreach (EquipSlot slot in LadderSlots) Assert.AreEqual(slot + " R75", Levels(picked, slot));
        }

        [Test]
        public void TopSet_AtLevel40_Nothing()
        {
            Assert.AreEqual(0, PickLadderAt(40).Count);
        }

        [Test]
        public void TopSet_AtLevel100_Rare75_Rare95_Epic100()
        {
            List<GearDictionary.Entry> picked = PickLadderAt(100);
            foreach (EquipSlot slot in LadderSlots) Assert.AreEqual(slot + " R75 R95 E100", Levels(picked, slot));
        }

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
        public void Gear_NoBand_OneTopPiecePerSlotAndRarity()
        {
            var entries = new List<GearDictionary.Entry>();
            foreach (EquipSlot slot in new[] { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape })
                foreach (byte level in new byte[] { 30, 60, 90 })
                {
                    entries.Add(Entry(slot, ItemRarity.Rare, level));
                    entries.Add(Entry(slot, ItemRarity.Rare, level)); // a second look of the same tier: not given twice
                    entries.Add(Entry(slot, ItemRarity.Epic, level));
                }
            List<GearDictionary.Entry> picked = Pick(entries); // lines with no band: each rarity is one bracket
            Assert.AreEqual(6 * 2, picked.Count);
            Assert.IsTrue(picked.TrueForAll(e => e.itemLevel == 90));
            List<GearDictionary.Entry> at60 = Pick(entries, 60); // the level-90 tier cannot be worn yet: the 60 is the top
            Assert.AreEqual(6 * 2, at60.Count);
            Assert.IsTrue(at60.TrueForAll(e => e.itemLevel == 60));
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
