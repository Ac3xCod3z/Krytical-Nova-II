using System.Collections.Generic;
using System.IO;
using KryticalNova.CharacterCreation;
using KryticalNova.EditorTools;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.UI.Menus;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The select screen's doll wears what the character was last played in: the roster keeps that record, a doll
    /// reads it, and the select scene's stage (and only that one) is told where gear is looked up.
    /// </summary>
    public sealed class SelectDollTests
    {
        private const string StoreProfile = "unittest-doll";

        private sealed class Lookup : IItemLookup
        {
            private readonly Dictionary<ushort, ItemDefinition> items = new Dictionary<ushort, ItemDefinition>();
            public ItemDefinition Get(ushort itemId) => items.TryGetValue(itemId, out ItemDefinition item) ? item : null;

            public ItemDefinition Add(ushort id, EquipSlot slot)
            {
                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = id;
                item.equipSlot = slot;
                items[id] = item;
                return item;
            }

            public void Destroy()
            {
                foreach (ItemDefinition item in items.Values) Object.DestroyImmediate(item);
            }
        }

        private ICharacterStore savedStore;

        [SetUp]
        public void UseAStoreOfItsOwn()
        {
            savedStore = CharacterSession.Store;
            CharacterSession.Store = new PlayerPrefsCharacterStore(StoreProfile);
        }

        [TearDown]
        public void PutTheStoreBack()
        {
            CharacterSession.Clear();
            new PlayerPrefsCharacterStore(StoreProfile).DeleteAll();
            CharacterSession.Store = savedStore;
        }

        // A character with a look: the session does not count one without as a character.
        private static CharacterRecipe Recipe(FactionId faction) => new CharacterRecipe
        {
            version = CharacterRecipe.CurrentVersion, faction = faction, lineage = 5, bodyType = BodyType.Feminine,
            parts = new ushort[] { 11, 22, 33 }, palette = 4, skinTone = 3,
        };

        private static WornPiece[] Outfit() => new[]
        {
            new WornPiece(EquipSlot.Weapon, 4101, 45),
            new WornPiece(EquipSlot.Armor, 1210, 0),
            new WornPiece(EquipSlot.Cape, 3301, 12),
        };

        [Test]
        public void TheRosterKeepsWhatACharacterWore()
        {
            var store = new PlayerPrefsCharacterStore(StoreProfile);
            var roster = new CharacterRoster();
            CharacterProfile made = CharacterProfile.Create("Astrid", Recipe(FactionId.Covenant));
            Assert.IsNotNull(made.worn, "a new character has an empty record, not none");
            Assert.AreEqual(0, made.worn.Length);
            made.worn = Outfit();
            roster.Set(1, made);
            store.Save(roster);

            CharacterProfile loaded = store.Load().Get(1);
            Assert.IsTrue(DollOutfit.Same(Outfit(), loaded.worn));
            Assert.AreEqual(45, loaded.worn[0].temper);
        }

        [Test]
        public void ARosterSavedBeforeGearWasKeptStillLoads()
        {
            // The roster as it was written before this field existed: no "worn" at all.
            const string old = "{\"version\":1,\"slots\":[{\"id\":\"0123456789abcdef0123456789abcdef\",\"name\":\"Old\",\"level\":7},{\"id\":\"\"},{\"id\":\"\"}],\"lastPlayedSlot\":0}";
            var roster = JsonUtility.FromJson<CharacterRoster>(old);
            roster.Normalize();
            CharacterProfile profile = roster.Get(0);
            Assert.AreEqual("Old", profile.name);
            Assert.AreEqual(7, profile.level);
            Assert.IsTrue(profile.worn == null || profile.worn.Length == 0);
            Assert.IsNull(DollOutfit.Find(profile.worn, EquipSlot.Weapon, new Lookup(), out _), "nothing to wear, and no error");
        }

        [Test]
        public void PlayingACharacterBringsItsRecordUpToDate()
        {
            var roster = new CharacterRoster();
            roster.Set(0, CharacterProfile.Create("Tolan", Recipe(FactionId.Hegemony)));
            roster.Set(2, CharacterProfile.Create("Mira", Recipe(FactionId.Hegemony)));
            CharacterSession.Store.Save(roster);
            CharacterSession.Enter(roster, 2);

            CharacterSession.RememberWorn(Outfit());
            CharacterRoster saved = CharacterSession.Store.Load();
            Assert.IsTrue(DollOutfit.Same(Outfit(), saved.Get(2).worn), "the character played");
            Assert.AreEqual(0, saved.Get(0).worn.Length, "nobody else's");

            // Gear taken off is taken off the record too.
            CharacterSession.RememberWorn(new[] { new WornPiece(EquipSlot.Armor, 1210, 0) });
            Assert.AreEqual(1, CharacterSession.Store.Load().Get(2).worn.Length);
        }

        [Test]
        public void ADollReadsEachSlotsPieceAndItsTemper()
        {
            var lookup = new Lookup();
            try
            {
                ItemDefinition weapon = lookup.Add(4101, EquipSlot.Weapon);
                ItemDefinition armor = lookup.Add(1210, EquipSlot.Armor);
                WornPiece[] worn = Outfit();

                Assert.AreSame(weapon, DollOutfit.Find(worn, EquipSlot.Weapon, lookup, out int temper));
                Assert.AreEqual(45, temper);
                Assert.AreSame(armor, DollOutfit.Find(worn, EquipSlot.Armor, lookup, out temper));
                Assert.AreEqual(0, temper);
                Assert.IsNull(DollOutfit.Find(worn, EquipSlot.Cape, lookup, out _), "an item that no longer exists is left off");
                Assert.IsNull(DollOutfit.Find(worn, EquipSlot.Boots, lookup, out _), "nothing worn there");
                Assert.IsNull(DollOutfit.Find(null, EquipSlot.Weapon, lookup, out _));

                Assert.IsTrue(DollOutfit.Same(worn, Outfit()));
                WornPiece[] tempered = Outfit();
                tempered[0].temper = 48;
                Assert.IsFalse(DollOutfit.Same(worn, tempered), "a Temper gained is a change");
                Assert.IsTrue(DollOutfit.Same(null, new WornPiece[0]));
                CollectionAssert.Contains(DollOutfit.OutfitSlots, EquipSlot.Cape);
                CollectionAssert.DoesNotContain(DollOutfit.OutfitSlots, EquipSlot.Weapon);
            }
            finally
            {
                lookup.Destroy();
            }
        }

        [Test]
        public void TheSelectScenesStageKnowsTheGear_TheCreatorsDoesNot()
        {
            Assert.AreEqual(1, Stages(FrontEnd.CharacterSelectScene, out int withGear), "one stage on character select");
            Assert.AreEqual(1, withGear, "it is told the item catalog and the Temper bands");
            Assert.AreEqual(1, Stages(FrontEnd.CharacterCreatorScene, out withGear), "one stage in the creator");
            Assert.AreEqual(0, withGear, "a character being made wears no gear");
        }

        /// <summary>How many showrooms a front-end scene has, and how many of them have both gear references set.</summary>
        private static int Stages(string sceneName, out int withGear)
        {
            string scene = File.ReadAllText(FrontEndBuilder.ScenePath(sceneName)).Replace("\r\n", "\n");
            int stages = 0;
            withGear = 0;
            foreach (string component in scene.Split(new[] { "\n--- !u!" }, System.StringSplitOptions.None))
            {
                if (!component.Contains("\n  fullBodyDistance:") || !component.Contains("\n  screenShiftX:")) continue;
                stages++;
                bool items = System.Text.RegularExpressions.Regex.IsMatch(component, @"\n  items: \{fileID: -?[1-9]");
                bool progression = System.Text.RegularExpressions.Regex.IsMatch(component, @"\n  progression: \{fileID: -?[1-9]");
                if (items && progression) withGear++;
            }
            return stages;
        }
    }
}
