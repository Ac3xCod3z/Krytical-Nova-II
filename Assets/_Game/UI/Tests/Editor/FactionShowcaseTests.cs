using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using KryticalNova.CharacterCreation;
using KryticalNova.EditorTools;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.UI.Menus;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Faction select's champions: each faction's has a lineage to be and a whole Epic set with its class weapon to
    /// wear, and the scene hands every skin's controller the showcase and the panel of words.
    /// </summary>
    public sealed class FactionShowcaseTests
    {
        private const int TopLevel = 112;

        private static readonly (FactionId faction, string lineage)[] Champions =
        {
            (FactionId.Hegemony, "Civilian"), (FactionId.Null, "Skeleton"), (FactionId.Covenant, "Elf"),
        };

        // The stances the scene builder gives them: each must be in the library, for the body they have.
        private static readonly (string group, string name)[] Stances = { ("Arms Folded", "Casual"), ("Posture", "Aggressive"), ("Posture", "Dramatic") };

        [Test]
        public void EveryChampionsStance_IsInTheLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(CharacterExpressionBaker.LibraryPath);
            Assert.NotNull(library);
            foreach ((string group, string name) in Stances)
            {
                Assert.IsTrue(FactionShowcase.TryGetStance(library, group, name, out CharacterExpressionLibrary.Motion stance), $"no stance '{group} / {name}'");
                Assert.IsTrue(stance.Has(BodyType.Masculine), $"'{group} / {name}' has no masculine clip");
            }
            Assert.IsFalse(FactionShowcase.TryGetStance(library, "Posture", "No Such Stance", out _));
        }

        [Test]
        public void EveryChampion_HasALineageAndAWholeEpicSet()
        {
            var database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(CharacterDataBaker.DatabasePath);
            var gear = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            Assert.NotNull(database);
            Assert.NotNull(gear);
            Assert.NotNull(items);

            foreach ((FactionId faction, string name) in Champions)
            {
                LineageDefinition lineage = FactionShowcase.FindLineage(database, faction, name);
                Assert.NotNull(lineage, $"{faction} has no lineage");
                Assert.AreEqual(name, lineage.displayName, $"{faction}'s champion is not the lineage asked for");

                var slots = new List<EquipSlot>(FactionShowcase.WornSlots) { EquipSlot.Weapon };
                foreach (EquipSlot slot in slots)
                {
                    var weaponClass = slot == EquipSlot.Weapon ? WeaponClassRules.DefaultFor(faction) : default;
                    ushort id = FactionShowcase.BestPiece(gear, slot, faction, lineage, weaponClass, TopLevel);
                    Assert.AreNotEqual(0, id, $"{faction} {name} has no Epic {slot} at or under level {TopLevel}");
                    ItemDefinition piece = items.Get(id);
                    Assert.NotNull(piece, $"{faction} {slot}: item {id} is not in the catalog");
                    Assert.AreEqual(ItemRarity.Epic, piece.rarity, $"{faction} {slot}: {piece.displayName}");
                    if (slot == EquipSlot.Weapon) Assert.NotNull(piece.weaponPrefab, $"{faction}'s weapon {piece.displayName} has no model");
                }
            }
        }

        [Test]
        public void ALowerLevel_GivesALowerPieceOrNone()
        {
            var database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(CharacterDataBaker.DatabasePath);
            var gear = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            LineageDefinition lineage = FactionShowcase.FindLineage(database, FactionId.Hegemony, "Civilian");
            ushort top = FactionShowcase.BestPiece(gear, EquipSlot.Armor, FactionId.Hegemony, lineage, default, TopLevel);
            ushort none = FactionShowcase.BestPiece(gear, EquipSlot.Armor, FactionId.Hegemony, lineage, default, 0);
            Assert.AreNotEqual(0, top);
            Assert.AreEqual(0, none, "nothing is level 0");
        }

        [Test]
        public void TheScene_HasOneShowcase_AndHandsItToEverySkin()
        {
            string scene = File.ReadAllText(FrontEndBuilder.ScenePath(FrontEnd.FactionSelectScene)).Replace("\r\n", "\n");
            int showcases = 0, controllers = 0;
            foreach (string component in scene.Split(new[] { "\n--- !u!" }, System.StringSplitOptions.None))
            {
                if (component.Contains("\n  champions:"))
                {
                    showcases++;
                    Assert.AreEqual(Champions.Length, Regex.Matches(component, @"\n  - faction: \d+").Count, "champions of the showcase");
                    foreach (string part in new[] { "database", "modelPrefab", "gear", "items", "progression", "viewCamera", "stage", "idleClip" })
                        StringAssert.IsMatch($@"\n  {part}: \{{fileID: -?[1-9]", component, $"the showcase has no '{part}'");
                }
                if (component.Contains("\n  cards:") && component.Contains("\n  showcase:"))
                {
                    controllers++;
                    foreach (string part in new[] { "screen", "database", "hotkeys", "showcase", "info" })
                        StringAssert.IsMatch($@"\n  {part}: \{{fileID: -?[1-9]", component, $"a faction select controller has no '{part}'");
                }
            }
            Assert.AreEqual(1, showcases, "one showcase in the scene, shared by the skins");
            Assert.AreEqual(Champions.Length, controllers, "one controller per faction skin");
        }
    }
}
