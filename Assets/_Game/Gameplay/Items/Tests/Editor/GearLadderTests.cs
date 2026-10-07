using System.Collections.Generic;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The baked gear ladder (GearDictionary, ItemCatalog, GearStatTable assets): the owner's Epic tables come out
    /// exactly, every band has its tiers, item IDs follow the line formula, and gear is locked to the right owners.
    /// Run "Krytical Nova/Items/6. Bake Gear Ladder" after changing a line or the stat table.
    /// </summary>
    public sealed class GearLadderTests
    {
        private const string Root = "Assets/_Game/Gameplay/Items/Data/Assets/";
        private const string ProgressionPath = "Assets/_Game/Gameplay/Progression/Data/Assets/CharacterData/CharacterProgression.asset";
        private static readonly int[] EpicLevels = { 100, 103, 106, 109, 112 };
        private static readonly string[] Packs = { "SCFI_SOLD", "SCFI_CIVL", "SCFI_ROBO", "FANT_SKTN", "VIKG_WARR", "ELVN_WARR" };

        private GearDictionary dictionary;
        private ItemCatalog catalog;
        private GearStatTable table;

        [OneTimeSetUp]
        public void LoadAssets()
        {
            dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(Root + "GearDictionary.asset");
            catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(Root + "ItemCatalog.asset");
            table = AssetDatabase.LoadAssetAtPath<GearStatTable>(Root + "GearStatTable.asset");
            Assert.IsNotNull(dictionary, "GearDictionary.asset");
            Assert.IsNotNull(catalog, "ItemCatalog.asset");
            Assert.IsNotNull(table, "GearStatTable.asset (bake the gear ladder)");
            Assert.IsTrue(dictionary.Lines.Count > 0, "The dictionary has no lines: generate the gear looks, then bake.");
        }

        // ---------------------------------------------------------------- the owner's Epic tables

        private void AssertEpic(EquipSlot slot, StatType stat, params float[] expected)
        {
            for (int i = 0; i < EpicLevels.Length; i++)
            {
                var items = catalog.Items.Where(d => d != null && d.IsLadderGear && d.equipSlot == slot
                                                     && d.rarity == ItemRarity.Epic && d.itemLevel == EpicLevels[i]).ToList();
                Assert.IsNotEmpty(items, $"No Epic {slot} at level {EpicLevels[i]}");
                foreach (var item in items)
                    Assert.AreEqual(expected[i], GearStatResolver.Total(item, stat), 0.001f, $"{item.name} {stat}");
            }
        }

        [Test]
        public void EpicWeaponsMatchTheOwnersTable()
        {
            AssertEpic(EquipSlot.Weapon, StatType.AttackPower, 884, 910, 937, 963, 990);
            AssertEpic(EquipSlot.Weapon, StatType.HitRate, 250, 258, 265, 273, 280);
            AssertEpic(EquipSlot.Weapon, StatType.ElementalAttack, 120, 128, 135, 143, 150);
        }

        [Test]
        public void EpicArmorMatchesTheOwnersTable()
        {
            AssertEpic(EquipSlot.Armor, StatType.DefensePower, 357, 368, 378, 389, 400);
            AssertEpic(EquipSlot.Armor, StatType.EvasionRate, 35, 36, 37, 38, 39);
            AssertEpic(EquipSlot.Armor, StatType.MaxHP, 80, 85, 90, 95, 100);
            AssertEpic(EquipSlot.Armor, StatType.ElementalDefense, 50, 52, 55, 58, 60);
        }

        [Test]
        public void EpicGlovesAndBootsMatchTheOwnersTables()
        {
            AssertEpic(EquipSlot.Gloves, StatType.DefensePower, 94, 97, 100, 102, 105);
            AssertEpic(EquipSlot.Gloves, StatType.HitRate, 100, 105, 110, 115, 120);
            AssertEpic(EquipSlot.Gloves, StatType.ElementalAttack, 30, 32, 35, 37, 40);
            AssertEpic(EquipSlot.Boots, StatType.DefensePower, 94, 97, 100, 102, 105);
            AssertEpic(EquipSlot.Boots, StatType.EvasionRate, 35, 36, 37, 38, 39);
        }

        [Test]
        public void EpicRingsAndAmuletsMatchTheOwnersTables()
        {
            AssertEpic(EquipSlot.Ring, StatType.Agility, 24, 25, 26, 27, 28);
            AssertEpic(EquipSlot.Ring, StatType.ElementalAttack, 100, 106, 112, 118, 125);
            AssertEpic(EquipSlot.Ring, StatType.CriticalDamagePercent, 18, 18, 19, 19, 20);
            AssertEpic(EquipSlot.Amulet, StatType.Spirit, 24, 25, 26, 27, 28);
            AssertEpic(EquipSlot.Amulet, StatType.ElementalDefense, 75, 78, 82, 86, 90);
            AssertEpic(EquipSlot.Amulet, StatType.KillExpRatePercent, 50, 52, 54, 56, 60);
            AssertEpic(EquipSlot.Amulet, StatType.KillExpAmountPercent, 100, 105, 110, 115, 120);
        }

        [Test]
        public void EpicSkillBonusIsFourThenFive()
        {
            foreach (var item in catalog.Items.Where(d => d != null && d.IsLadderGear && d.rarity == ItemRarity.Epic
                                                          && (d.equipSlot == EquipSlot.Weapon || d.equipSlot == EquipSlot.Gloves
                                                              || d.equipSlot == EquipSlot.Boots || d.equipSlot == EquipSlot.Ring)))
            {
                Assert.AreEqual(1, item.skillModifiers.Length, item.name);
                Assert.AreEqual(item.itemLevel >= 109 ? 5 : 4, item.skillModifiers[0].levels, item.name);
            }
        }

        // ---------------------------------------------------------------- bands and IDs

        [Test]
        public void EveryRarityHasItsLevelTiers()
        {
            var expected = new Dictionary<ItemRarity, int[]>
            {
                { ItemRarity.Common, new[] { 1, 4, 7, 10, 13, 16, 19, 22, 25, 28 } },
                { ItemRarity.Uncommon, new[] { 20, 25, 30, 35, 40 } },
                { ItemRarity.Rare, new[] { 45, 55, 65, 75, 85, 95, 105, 110 } },
                { ItemRarity.Epic, new[] { 100, 103, 106, 109, 112 } },
            };
            foreach (var pair in expected)
            {
                var levels = dictionary.Entries.Where(e => e.rarity == pair.Key && e.slot == EquipSlot.Armor)
                    .Select(e => (int)e.itemLevel).Distinct().OrderBy(l => l).ToArray();
                CollectionAssert.AreEqual(pair.Value, levels, $"{pair.Key} armor levels");
            }
            Assert.IsFalse(dictionary.Entries.Any(e => e.rarity == ItemRarity.Legendary), "Legendary gear is not in the game yet");
        }

        [Test]
        public void RareHasTwoLooksAndEveryOtherRarityOne()
        {
            foreach (string pack in Packs)
            {
                int Looks(ItemRarity rarity) => dictionary.Entries.Where(e => e.pack == pack && e.slot == EquipSlot.Armor && e.rarity == rarity)
                    .Select(e => e.lineId).Distinct().Count();
                Assert.AreEqual(1, Looks(ItemRarity.Common), $"{pack} Common looks");
                Assert.AreEqual(1, Looks(ItemRarity.Uncommon), $"{pack} Uncommon looks");
                Assert.AreEqual(2, Looks(ItemRarity.Rare), $"{pack} Rare looks");
                Assert.AreEqual(1, Looks(ItemRarity.Epic), $"{pack} Epic looks");
                // The first Rare look covers 45-75 (four tiers), the second 85-110 (four tiers).
                var first = dictionary.Entries.Where(e => e.pack == pack && e.slot == EquipSlot.Armor && e.rarity == ItemRarity.Rare && e.itemLevel <= 75)
                    .Select(e => e.lineId).Distinct().ToList();
                Assert.AreEqual(1, first.Count, $"{pack} Rare 45-75 shares one look");
            }
        }

        [Test]
        public void TiersOfALineShareOneLookAndOneName()
        {
            foreach (var line in dictionary.Entries.GroupBy(e => e.lineId))
            {
                var first = line.First();
                var firstItem = catalog.Get(first.itemId);
                Assert.IsNotNull(firstItem, $"Item {first.itemId} is in the dictionary but not the catalog");
                foreach (var entry in line)
                {
                    var item = catalog.Get(entry.itemId);
                    Assert.IsNotNull(item, $"Item {entry.itemId}");
                    Assert.AreEqual(firstItem.icon, item.icon, item.name);
                    Assert.AreEqual(firstItem.displayName, item.displayName, item.name);
                    Assert.AreEqual(firstItem.weaponPrefab, item.weaponPrefab, item.name);
                    CollectionAssert.AreEqual(first.partIds, entry.partIds, item.name);
                    Assert.AreEqual(entry.itemLevel, item.itemLevel, item.name);
                    Assert.AreEqual(entry.rarity, item.rarity, item.name);
                }
            }
        }

        [Test]
        public void EveryPieceOfASetCarriesItsName()
        {
            var owners = new Dictionary<string, string>();
            foreach (var group in dictionary.Lines.Where(l => l.IsValid && l.slot != EquipSlot.Weapon).GroupBy(l => $"{l.tag} band {l.band}"))
            {
                var sets = group.Select(l => l.set).Distinct().ToList();
                Assert.AreEqual(1, sets.Count, $"{group.Key} names more than one set: {string.Join(", ", sets)}");
                string set = sets[0];
                Assert.IsFalse(string.IsNullOrEmpty(set), $"{group.Key} names no set");
                foreach (var line in group)
                {
                    StringAssert.StartsWith(set + " ", line.displayName, $"Line {line.lineId} of {group.Key}");
                    StringAssert.DoesNotContain("Mark ", line.displayName, $"Line {line.lineId} of {group.Key}");
                    foreach (var entry in dictionary.Entries.Where(e => e.lineId == line.lineId))
                        Assert.AreEqual(line.displayName, catalog.Get(entry.itemId).displayName, $"Item {entry.itemId}: bake the gear ladder");
                }
                Assert.IsFalse(owners.TryGetValue(set, out string other), $"{group.Key} and {other} are both the {set} set");
                owners[set] = group.Key;
            }
        }

        [Test]
        public void ItemIdsComeFromTheLineAndNeverCollide()
        {
            var seen = new HashSet<ushort>();
            foreach (var line in dictionary.Entries.GroupBy(e => e.lineId))
            {
                Assert.IsTrue(dictionary.TryGetLine(line.Key, out var definition), $"Line {line.Key}");
                int tier = 0;
                foreach (var entry in line.OrderBy(e => e.itemLevel))
                {
                    Assert.AreEqual(definition.FirstItemId + tier, entry.itemId, $"Line {line.Key} tier {tier}");
                    Assert.IsTrue(seen.Add(entry.itemId), $"Item ID {entry.itemId} twice");
                    Assert.GreaterOrEqual(entry.itemId, GearDictionary.LadderIdBase);
                    tier++;
                }
                Assert.LessOrEqual(tier, GearDictionary.IdsPerLine);
            }
        }

        [Test]
        public void NothingAsksForMoreThanTheLevelCap()
        {
            var progression = AssetDatabase.LoadAssetAtPath<CharacterProgressionData>(ProgressionPath);
            Assert.IsNotNull(progression);
            Assert.AreEqual(112, progression.MaxLevel, "The player level cap");
            Assert.LessOrEqual(table.MaxItemLevel, progression.MaxLevel);
            Assert.AreEqual(112, dictionary.Entries.Max(e => (int)e.itemLevel), "The top Epic tier is the cap gear");
        }

        // ---------------------------------------------------------------- who may use what

        [Test]
        public void EveryClassHasWeaponsLockedToItsFaction()
        {
            foreach (var weaponClass in WeaponClasses.All)
            {
                var weapons = dictionary.Entries.Where(e => e.slot == EquipSlot.Weapon && e.weaponClass == weaponClass).ToList();
                Assert.AreEqual(28, weapons.Count, $"{weaponClass}: 10 Common + 5 Uncommon + 8 Rare + 5 Epic tiers");
                var faction = WeaponClassRules.FactionOf(weaponClass);
                foreach (var weapon in weapons)
                {
                    Assert.AreEqual(FactionRules.MaskOf(faction), weapon.factions, $"{weapon.name} ({weaponClass})");
                    Assert.AreEqual(weaponClass, catalog.Get(weapon.itemId).weaponClass, weapon.name);
                    foreach (var other in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                        Assert.AreEqual(other == faction, dictionary.CanUse(weapon.itemId, other, BodyFit.Humanoid, out _), $"{weapon.name} for {other}");
                }
                Assert.IsTrue(dictionary.TryFindStarter(EquipSlot.Weapon, faction, BodyFit.None, null, weaponClass, out var starter), $"{weaponClass} starter");
                Assert.AreEqual(1, starter.itemLevel);
                Assert.AreEqual(ItemRarity.Common, starter.rarity);
            }
            Assert.IsFalse(dictionary.Entries.Any(e => e.slot != EquipSlot.Weapon && e.weaponClass != WeaponClassId.None), "Only weapons carry a class");
        }

        [Test]
        public void EveryGroupHasACompleteStarterSet()
        {
            foreach (string pack in Packs)
            {
                var any = dictionary.Entries.First(e => e.pack == pack && e.slot == EquipSlot.Armor);
                FactionId faction = new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant }.First(f => FactionRules.Includes(any.factions, f));
                foreach (var slot in new[] { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape, EquipSlot.Ring, EquipSlot.Amulet })
                {
                    Assert.IsTrue(dictionary.TryFindStarter(slot, faction, any.bodies, pack, WeaponClassId.None, out var starter), $"{pack} {slot}");
                    Assert.AreEqual(pack, starter.pack, $"{pack} {slot} comes from its own group");
                    Assert.AreEqual(1, starter.itemLevel, $"{pack} {slot}");
                    Assert.AreEqual(ItemRarity.Common, starter.rarity, $"{pack} {slot}");
                }
            }
        }

        [Test]
        public void RingsAndAmuletsBelongToAGroupLikeOutfits()
        {
            foreach (var slot in new[] { EquipSlot.Ring, EquipSlot.Amulet })
                foreach (string pack in Packs)
                    Assert.AreEqual(28, dictionary.Entries.Count(e => e.slot == slot && e.pack == pack), $"{pack} {slot} tiers");
        }

        [Test]
        public void LookupSnapsToTheTierAtOrBelowTheLevel()
        {
            Assert.IsTrue(dictionary.TryFindItem(EquipSlot.Weapon, ItemRarity.Rare, 47, FactionId.Hegemony, BodyFit.None, null, WeaponClassId.Carbine, out var found));
            Assert.AreEqual(45, found.itemLevel);
            Assert.AreEqual(WeaponClassId.Carbine, found.weaponClass);
            Assert.IsTrue(dictionary.TryGetNextTier(found.itemId, out var next));
            Assert.AreEqual(55, next.itemLevel);
            Assert.AreEqual(found.lineId, next.lineId);
        }

        [Test]
        public void EveryLadderItemGivesStats()
        {
            foreach (var item in catalog.Items.Where(d => d != null && d.IsLadderGear))
            {
                Assert.IsTrue(item.baseStats != null && item.baseStats.Length > 0, $"{item.name} has no primary stat");
                Assert.Greater(item.itemLevel, 0, item.name);
                Assert.IsNotNull(item.icon, $"{item.name} has no icon (capture the missing icons, then bake)");
            }
        }
    }
}
